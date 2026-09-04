using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Services;

public sealed class GitRemoteService : IGitRemoteService
{
    private const char RecordSeparator = '\x1e';
    private const char UnitSeparator = '\x1f';
    private readonly IGitTagService _tagService;
    private readonly IGitConfigService _gitConfigService;
    private readonly IGitCommandRunner _commandRunner;
    private readonly IExecutionContextService? _executionContextService;

    public GitRemoteService(
        IGitTagService tagService,
        IGitConfigService gitConfigService,
        IGitCommandRunner? commandRunner = null,
        IExecutionContextService? executionContextService = null)
    {
        _tagService = tagService;
        _gitConfigService = gitConfigService;
        _commandRunner = commandRunner ?? new GitCommandRunner();
        _executionContextService = executionContextService;
    }

    public async Task<IReadOnlyList<GitRemote>> GetRemotesAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default)
    {
        GitRemoteOperationResult output = await RunGitAsync(
            repository,
            false,
            cancellationToken,
            "remote",
            "-v");
        return ParseRemotes(output.Output);
    }

    public async Task<GitCurrentBranchRemoteStatus> GetCurrentBranchRemoteStatusAsync(
        RepositoryInfo repository,
        GitRemote? defaultRemote,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LocalBranchReference> localBranches = await GetLocalBranchReferencesAsync(
            repository,
            cancellationToken);
        LocalBranchReference? currentBranch = localBranches.FirstOrDefault(branch => branch.IsCurrent);
        if (currentBranch is null)
        {
            return new GitCurrentBranchRemoteStatus(false, null, null);
        }

        IReadOnlyDictionary<string, string> branchPushRemotes =
            await _gitConfigService.GetBranchPushRemotesAsync(repository);
        string pushDefaultRemote = await _gitConfigService.GetPushDefaultRemoteAsync(
            ConfigScope.None,
            repository);
        string defaultRemoteName = defaultRemote?.Name
            ?? (currentBranch.HasUpstream ? currentBranch.UpstreamRemoteName : "");
        BranchRemoteSelection selection = CreateBranchRemoteSelection(
            currentBranch,
            defaultRemoteName,
            branchPushRemotes,
            pushDefaultRemote);
        IReadOnlySet<string> remoteTrackingBranches = await GetAllRemoteTrackingBranchesAsync(
            repository,
            cancellationToken);

        GitRemoteBranchStatus? trackingTarget = await CreateRemoteBranchStatusAsync(
            repository,
            currentBranch.Name,
            selection.PullRemoteName,
            selection.PullTrackingBranch,
            remoteTrackingBranches,
            currentBranch.HasUpstream,
            cancellationToken);
        GitRemoteBranchStatus? pushTarget = selection.HasPushRemoteOverride
            ? await CreateRemoteBranchStatusAsync(
                repository,
                currentBranch.Name,
                selection.PushRemoteName,
                selection.PushTrackingBranch,
                remoteTrackingBranches,
                includeUnpublished: true,
                cancellationToken)
            : null;

        return new GitCurrentBranchRemoteStatus(
            currentBranch.HasUpstream,
            trackingTarget,
            pushTarget);
    }

    public async Task<GitCommitPage> GetCommitsPageAsync(
        RepositoryInfo repository,
        string revisionRange,
        int skip,
        int count)
    {
        if (string.IsNullOrWhiteSpace(revisionRange) || revisionRange.StartsWith('-'))
        {
            throw new ArgumentException("A valid Git revision range is required.", nameof(revisionRange));
        }

        return await GetCommitsCoreAsync(repository, [revisionRange], skip, count);
    }

    public async Task<GitCommitPage> GetComparisonCommitsPageAsync(
        RepositoryInfo repository,
        string leftRevision,
        string rightRevision,
        string leftLabel,
        string rightLabel,
        int skip,
        int count)
    {
        ValidateRevision(leftRevision, nameof(leftRevision));
        ValidateRevision(rightRevision, nameof(rightRevision));
        ValidatePageArguments(skip, count);

        int requestedCount = count + 1;
        string revisionRange = $"{leftRevision}...{rightRevision}";
        Task<GitRemoteOperationResult> outputTask = RunGitAsync(
            repository,
            false,
            "log",
            "--left-right",
            $"--skip={skip}",
            $"--max-count={requestedCount}",
            "--date=iso-strict",
            "--pretty=format:%m%x1f%H%x1f%h%x1f%an%x1f%ae%x1f%cn%x1f%ce%x1f%ad%x1f%s%x1f%B%x1f%P%x1e",
            revisionRange);
        Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> changedFilePathsTask =
            GetChangedFilePathsByCommitAsync(repository, [revisionRange], skip, requestedCount);
        await Task.WhenAll(outputTask, changedFilePathsTask);
        IReadOnlyList<GitCommit> commits = ParseComparisonCommits(
            (await outputTask).Output,
            leftLabel,
            rightLabel,
            await changedFilePathsTask);
        return CreateCommitPage(commits, count);
    }

    public async Task<GitCommitPage> GetOutgoingCommitsPageAsync(
        RepositoryInfo repository,
        GitRemote remote,
        BranchSynchronizationItem branch,
        int skip,
        int count)
    {
        ValidateReferenceName(branch.Name, nameof(branch));
        if (branch.IsPublishedToPushRemote)
        {
            return await GetCommitsPageAsync(
                repository,
                branch.OutgoingRevisionRange,
                skip,
                count);
        }

        string remoteName = string.IsNullOrWhiteSpace(branch.ConfiguredPushRemoteName)
            ? remote.Name
            : branch.ConfiguredPushRemoteName;
        ValidateReferenceName(remoteName, nameof(remote));
        return await GetCommitsCoreAsync(
            repository,
            [$"refs/heads/{branch.Name}", "--not", $"--remotes={remoteName}"],
            skip,
            count);
    }

    public async Task<GitCommitPage> GetIncomingCommitsPageAsync(
        RepositoryInfo repository,
        BranchSynchronizationItem branch,
        int skip,
        int count)
    {
        ValidatePageArguments(skip, count);
        if (!branch.IsPublishedToRemote)
        {
            return new GitCommitPage([], false);
        }

        return await GetCommitsPageAsync(
            repository,
            branch.IncomingRevisionRange,
            skip,
            count);
    }

    public async Task<SynchronizationSnapshot> GetSynchronizationSnapshotAsync(
        RepositoryInfo repository,
        GitRemote remote,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LocalBranchReference> localBranches = await GetLocalBranchReferencesAsync(
            repository,
            cancellationToken);
        IReadOnlyDictionary<string, string> branchPushRemotes =
            await _gitConfigService.GetBranchPushRemotesAsync(repository);
        IReadOnlySet<string> remoteTrackingBranches = await GetRemoteTrackingBranchesAsync(
            repository,
            remote,
            cancellationToken);
        IReadOnlyList<BranchSynchronizationItem> branches = await GetBranchSynchronizationItemsAsync(
            repository,
            remote,
            localBranches,
            branchPushRemotes,
            remoteTrackingBranches,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<GitTag> localTags = await _tagService.GetLocalTagsAsync(repository);
        IReadOnlyList<GitTag> remoteTags = await GetRemoteTagsAsync(repository, remote, cancellationToken);
        IReadOnlyDictionary<string, GitTag> remoteTagsByName = remoteTags
            .ToDictionary(tag => tag.RemoteTagName, StringComparer.Ordinal);
        IReadOnlyList<TagSynchronizationItem> tags = CreateTagSynchronizationItems(
            localTags,
            remoteTagsByName.ToDictionary(
                item => item.Key,
                item => (item.Value.ReferenceObjectHash, item.Value.ObjectHash),
                StringComparer.Ordinal));

        return new SynchronizationSnapshot(remote, branches, tags);
    }

    public async Task<SynchronizationSnapshot> GetLocalSynchronizationSnapshotAsync(
        RepositoryInfo repository,
        GitRemote remote,
        IReadOnlyList<TagSynchronizationItem> knownTags,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LocalBranchReference> localBranches = await GetLocalBranchReferencesAsync(
            repository,
            cancellationToken);
        IReadOnlyDictionary<string, string> branchPushRemotes =
            await _gitConfigService.GetBranchPushRemotesAsync(repository);
        IReadOnlySet<string> remoteTrackingBranches = await GetRemoteTrackingBranchesAsync(
            repository,
            remote,
            cancellationToken);
        IReadOnlyList<BranchSynchronizationItem> branches = await GetBranchSynchronizationItemsAsync(
            repository,
            remote,
            localBranches,
            branchPushRemotes,
            remoteTrackingBranches,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<GitTag> localTags = await _tagService.GetLocalTagsAsync(repository);
        IReadOnlyDictionary<string, (string ReferenceObjectHash, string ObjectHash)> knownRemoteTags = knownTags
            .Where(tag => tag.IsPublishedToRemote)
            .ToDictionary(
                tag => tag.Name,
                tag => (tag.RemoteReferenceObjectHash, tag.RemoteObjectHash),
                StringComparer.Ordinal);
        IReadOnlyList<TagSynchronizationItem> tags = CreateTagSynchronizationItems(localTags, knownRemoteTags);

        return new SynchronizationSnapshot(remote, branches, tags);
    }

    public async Task<SynchronizationSnapshot> GetConfiguredSynchronizationSnapshotAsync(
        RepositoryInfo repository,
        GitRemote defaultRemote,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BranchSynchronizationItem> branches =
            await GetConfiguredBranchSynchronizationItemsAsync(
                repository,
                defaultRemote,
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<GitTag> localTags = await _tagService.GetLocalTagsAsync(repository);
        IReadOnlyList<GitTag> remoteTags = await GetRemoteTagsAsync(
            repository,
            defaultRemote,
            cancellationToken);
        IReadOnlyDictionary<string, GitTag> remoteTagsByName = remoteTags
            .ToDictionary(tag => tag.RemoteTagName, StringComparer.Ordinal);
        IReadOnlyList<TagSynchronizationItem> tags = CreateTagSynchronizationItems(
            localTags,
            remoteTagsByName.ToDictionary(
                item => item.Key,
                item => (item.Value.ReferenceObjectHash, item.Value.ObjectHash),
                StringComparer.Ordinal));

        return new SynchronizationSnapshot(defaultRemote, branches, tags);
    }

    public async Task<SynchronizationSnapshot> GetLocalConfiguredSynchronizationSnapshotAsync(
        RepositoryInfo repository,
        GitRemote defaultRemote,
        IReadOnlyList<TagSynchronizationItem> knownTags,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BranchSynchronizationItem> branches =
            await GetConfiguredBranchSynchronizationItemsAsync(
                repository,
                defaultRemote,
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<GitTag> localTags = await _tagService.GetLocalTagsAsync(repository);
        IReadOnlyDictionary<string, (string ReferenceObjectHash, string ObjectHash)> knownRemoteTags = knownTags
            .Where(tag => tag.IsPublishedToRemote)
            .ToDictionary(
                tag => tag.Name,
                tag => (tag.RemoteReferenceObjectHash, tag.RemoteObjectHash),
                StringComparer.Ordinal);
        IReadOnlyList<TagSynchronizationItem> tags = CreateTagSynchronizationItems(localTags, knownRemoteTags);

        return new SynchronizationSnapshot(defaultRemote, branches, tags);
    }

    public async Task<DateTimeOffset?> GetLastFetchTimeAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default)
    {
        GitRemoteOperationResult result = await RunGitAsync(
            repository,
            false,
            cancellationToken,
            "rev-parse",
            "--path-format=absolute",
            "--git-path",
            "FETCH_HEAD");
        string fetchHeadPath = result.Output.Trim();
        if (string.IsNullOrWhiteSpace(fetchHeadPath))
        {
            return null;
        }

        if (_executionContextService is not null)
        {
            RepositoryFileMetadata? metadata = await _executionContextService.Current.Runtime.Files.GetMetadataAsync(
                fetchHeadPath,
                cancellationToken);
            return metadata?.LastWriteTime.ToLocalTime();
        }

        if (!Path.IsPathFullyQualified(fetchHeadPath))
        {
            fetchHeadPath = Path.GetFullPath(Path.Combine(repository.Path, fetchHeadPath));
        }
        return File.Exists(fetchHeadPath)
            ? new DateTimeOffset(File.GetLastWriteTimeUtc(fetchHeadPath), TimeSpan.Zero).ToLocalTime()
            : null;
    }

    public async Task<GitRemoteOperationResult> FetchAsync(
        RepositoryInfo repository,
        GitRemote remote,
        CancellationToken cancellationToken = default)
    {
        return await FetchRemoteAsync(
            repository,
            remote.Name,
            fetchTags: true,
            cancellationToken);
    }

    public async Task<GitRemoteOperationResult> FetchBranchesAsync(
        RepositoryInfo repository,
        GitRemote remote,
        CancellationToken cancellationToken = default)
    {
        return await FetchRemoteAsync(
            repository,
            remote.Name,
            fetchTags: false,
            cancellationToken);
    }

    public async Task<GitRemoteOperationResult> FetchSynchronizationRemotesAsync(
        RepositoryInfo repository,
        GitRemote defaultRemote,
        CancellationToken cancellationToken = default)
    {
        IReadOnlySet<string> remoteNames = await GetSynchronizationRemoteNamesAsync(
            repository,
            defaultRemote,
            cancellationToken);
        List<string> outputs = [];
        foreach (string remoteName in remoteNames
            .OrderByDescending(name => string.Equals(name, defaultRemote.Name, StringComparison.Ordinal))
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            GitRemoteOperationResult result = await FetchRemoteAsync(
                repository,
                remoteName,
                fetchTags: string.Equals(remoteName, defaultRemote.Name, StringComparison.Ordinal),
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(result.Output))
            {
                outputs.Add(result.Output.Trim());
            }
        }

        return new GitRemoteOperationResult(
            string.Join(Environment.NewLine + Environment.NewLine, outputs));
    }

    private Task<GitRemoteOperationResult> FetchRemoteAsync(
        RepositoryInfo repository,
        string remoteName,
        bool fetchTags,
        CancellationToken cancellationToken)
    {
        ValidateReferenceName(remoteName, nameof(remoteName));
        return RunGitAsync(
            repository,
            false,
            cancellationToken,
            "fetch",
            "--prune",
            fetchTags ? "--tags" : "--no-tags",
            "--progress",
            remoteName);
    }

    public async Task<GitRemoteOperationResult> PullAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default)
    {
        return await RunGitAsync(repository, false, cancellationToken, "pull", "--progress");
    }

    public async Task<GitRemoteOperationResult> PullAsync(
        RepositoryInfo repository,
        string remoteName,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(remoteName, nameof(remoteName));
        ValidateReferenceName(branchName, nameof(branchName));
        return await RunGitAsync(
            repository,
            false,
            cancellationToken,
            "pull",
            "--progress",
            remoteName,
            $"refs/heads/{branchName}");
    }

    public async Task<GitRemoteOperationResult> PushBranchAsync(
        RepositoryInfo repository,
        string remoteName,
        string branchName,
        bool forceWithLease,
        CancellationToken cancellationToken = default)
    {
        GitPushRequest request = new(
            remoteName,
            [
                new GitPushReferenceUpdate(
                    GitPushReferenceKind.Branch,
                    branchName,
                    forceWithLease)
            ],
            GitPushMode.Regular);
        return await PushAsync(repository, request, cancellationToken);
    }

    public async Task<GitRemoteOperationResult> PushTagAsync(
        RepositoryInfo repository,
        GitRemote remote,
        string tagName,
        CancellationToken cancellationToken = default)
    {
        GitPushRequest request = new(
            remote.Name,
            [new GitPushReferenceUpdate(GitPushReferenceKind.Tag, tagName)],
            GitPushMode.Regular);
        return await PushAsync(repository, request, cancellationToken);
    }

    public async Task<GitRemoteOperationResult> PushAsync(
        RepositoryInfo repository,
        GitPushRequest request,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> arguments = GitPushArguments.Create(request);
        return await RunGitAsync(
            repository,
            false,
            cancellationToken,
            arguments.ToArray());
    }

    public async Task<GitRemoteOperationResult> AddRemoteAsync(
        RepositoryInfo repository,
        string name,
        string url,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(name, nameof(name));
        return await RunGitAsync(repository, false, cancellationToken, "remote", "add", name, url);
    }

    public async Task<GitRemoteOperationResult> SetRemoteUrlAsync(RepositoryInfo repository, GitRemote remote, string url)
    {
        return await RunGitAsync(repository, false, "remote", "set-url", remote.Name, url);
    }

    public async Task<GitRemoteOperationResult> RenameRemoteAsync(
        RepositoryInfo repository,
        GitRemote remote,
        string newName)
    {
        return await RunGitAsync(repository, false, "remote", "rename", remote.Name, newName);
    }

    public async Task<GitRemoteOperationResult> RemoveRemoteAsync(
        RepositoryInfo repository,
        GitRemote remote,
        CancellationToken cancellationToken = default)
    {
        ValidateReferenceName(remote.Name, nameof(remote));
        return await RunGitAsync(repository, false, cancellationToken, "remote", "remove", remote.Name);
    }

    public async Task<GitRemoteOperationResult> DeleteBranchAsync(
        RepositoryInfo repository,
        GitRemote remote,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        var deleteResult = await RunGitAsync(repository, false, cancellationToken, "push", "--progress", remote.Name, "--delete", branchName);
        var pruneResult = await RunGitAsync(repository, false, cancellationToken, "fetch", "--prune", "--progress", remote.Name);
        var output = CombineOutput(deleteResult.Output, pruneResult.Output);
        return new GitRemoteOperationResult(output);
    }

    public Task<GitRemoteOperationResult> FetchTagAsync(
        RepositoryInfo repository,
        GitRemote remote,
        string tagName,
        bool force,
        CancellationToken cancellationToken = default)
    {
        string prefix = force ? "+" : "";
        string refspec = $"{prefix}refs/tags/{tagName}:refs/tags/{tagName}";
        return RunGitAsync(repository, false, cancellationToken, "fetch", "--no-tags", "--progress", remote.Name, refspec);
    }

    public async Task<IReadOnlyList<GitTag>> GetRemoteTagsAsync(
        RepositoryInfo repository,
        GitRemote remote,
        CancellationToken cancellationToken = default)
    {
        GitRemoteOperationResult output = await RunGitAsync(
            repository,
            false,
            cancellationToken,
            "ls-remote",
            "--tags",
            remote.Name);
        return ParseRemoteTags(output.Output, remote);
    }

    public async Task<GitRemoteOperationResult> DeleteTagAsync(
        RepositoryInfo repository,
        GitRemote remote,
        string tagName,
        CancellationToken cancellationToken = default)
    {
        var deleteResult = await RunGitAsync(repository, false, cancellationToken, "push", "--progress", remote.Name, $":refs/tags/{tagName}");
        var fetchResult = await RunGitAsync(repository, false, cancellationToken, "fetch", "--prune", "--tags", "--progress", remote.Name);
        var output = CombineOutput(deleteResult.Output, fetchResult.Output);
        return new GitRemoteOperationResult(output);
    }

    private async Task<IReadOnlyList<LocalBranchReference>> GetLocalBranchReferencesAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        GitRemoteOperationResult output = await RunGitAsync(
            repository,
            false,
            cancellationToken,
            "for-each-ref",
            "refs/heads",
            $"--format=%(refname:short){UnitSeparator}%(HEAD){UnitSeparator}%(upstream:short){UnitSeparator}%(upstream:remotename){UnitSeparator}%(upstream:trackshort){UnitSeparator}%(push:remotename){UnitSeparator}%(push:short){UnitSeparator}%(push:trackshort){UnitSeparator}%(worktreepath)");

        List<LocalBranchReference> branches = [];
        foreach (string rawLine in output.Output.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] fields = line.Split(UnitSeparator);
            if (fields.Length < 2 || string.IsNullOrWhiteSpace(fields[0]))
            {
                continue;
            }

            branches.Add(new LocalBranchReference(
                fields[0],
                fields[1] == "*",
                fields.Length > 2 ? fields[2] : "",
                fields.Length > 3 ? fields[3] : "",
                fields.Length > 4 ? fields[4] : "",
                fields.Length > 5 ? fields[5] : "",
                fields.Length > 6 ? fields[6] : "",
                fields.Length > 7 ? fields[7] : "",
                fields.Length > 8 ? fields[8] : ""));
        }

        return branches;
    }

    private async Task<IReadOnlySet<string>> GetRemoteTrackingBranchesAsync(
        RepositoryInfo repository,
        GitRemote remote,
        CancellationToken cancellationToken)
    {
        GitRemoteOperationResult output = await RunGitAsync(
            repository,
            false,
            cancellationToken,
            "for-each-ref",
            $"refs/remotes/{remote.Name}",
            "--format=%(refname:short)");

        return output.Output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(reference => reference.Trim())
            .Where(reference => !reference.EndsWith("/HEAD", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task<IReadOnlyList<BranchSynchronizationItem>> GetBranchSynchronizationItemsAsync(
        RepositoryInfo repository,
        GitRemote remote,
        IReadOnlyList<LocalBranchReference> localBranches,
        IReadOnlyDictionary<string, string> branchPushRemotes,
        IReadOnlySet<string> remoteTrackingBranches,
        CancellationToken cancellationToken)
    {
        string remotePrefix = $"{remote.Name}/";
        List<BranchSynchronizationItem> branches = [];
        foreach (LocalBranchReference localBranch in localBranches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool tracksSelectedRemote = localBranch.UpstreamBranch.StartsWith(
                remotePrefix,
                StringComparison.Ordinal);
            string remoteTrackingBranch = tracksSelectedRemote
                ? localBranch.UpstreamBranch
                : $"{remotePrefix}{localBranch.Name}";
            bool isPublishedToRemote = remoteTrackingBranches.Contains(remoteTrackingBranch);
            (int AheadCount, int BehindCount) counts = isPublishedToRemote
                ? await GetRevisionCountsAsync(
                    repository,
                    localBranch.Name,
                    remoteTrackingBranch,
                    cancellationToken)
                : (0, 0);
            branchPushRemotes.TryGetValue(localBranch.Name, out string? explicitPushRemoteName);
            bool hasPushRemoteOverride = !string.IsNullOrWhiteSpace(explicitPushRemoteName)
                || (!string.IsNullOrWhiteSpace(localBranch.PushRemoteName)
                    && !string.Equals(
                        localBranch.PushRemoteName,
                        localBranch.UpstreamRemoteName,
                        StringComparison.Ordinal));

            branches.Add(new BranchSynchronizationItem(
                localBranch.Name,
                localBranch.IsCurrent,
                localBranch.UpstreamBranch,
                localBranch.UpstreamRemoteName,
                localBranch.UpstreamTrackingState,
                localBranch.PushRemoteName,
                explicitPushRemoteName ?? "",
                localBranch.PushTrackingBranch,
                localBranch.PushTrackingState,
                0,
                0,
                hasPushRemoteOverride,
                remoteTrackingBranch,
                tracksSelectedRemote,
                isPublishedToRemote,
                counts.AheadCount,
                counts.BehindCount,
                localBranch.WorktreePath));
        }

        return branches
            .OrderByDescending(branch => branch.IsCurrent)
            .ThenBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<BranchSynchronizationItem>>
        GetConfiguredBranchSynchronizationItemsAsync(
            RepositoryInfo repository,
            GitRemote defaultRemote,
            CancellationToken cancellationToken)
    {
        IReadOnlyList<BranchRemoteSelection> remoteSelections =
            await GetBranchRemoteSelectionsAsync(repository, defaultRemote, cancellationToken);
        IReadOnlySet<string> remoteTrackingBranches = await GetAllRemoteTrackingBranchesAsync(
            repository,
            cancellationToken);

        List<BranchSynchronizationItem> branches = [];
        foreach (BranchRemoteSelection selection in remoteSelections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LocalBranchReference localBranch = selection.Branch;
            string pullTrackingBranch = selection.PullTrackingBranch;
            bool isPublishedToPullRemote = remoteTrackingBranches.Contains(pullTrackingBranch);
            (int AheadCount, int BehindCount) pullCounts = isPublishedToPullRemote
                ? await GetRevisionCountsAsync(
                    repository,
                    localBranch.Name,
                    pullTrackingBranch,
                    cancellationToken)
                : (0, 0);
            string pullTrackingState = CreateTrackingState(
                isPublishedToPullRemote,
                pullCounts.AheadCount,
                pullCounts.BehindCount);

            string pushTrackingBranch = selection.PushTrackingBranch;
            bool isPublishedToPushRemote = remoteTrackingBranches.Contains(pushTrackingBranch);
            (int AheadCount, int BehindCount) pushCounts;
            if (!isPublishedToPushRemote)
            {
                pushCounts = (0, 0);
            }
            else if (string.Equals(pushTrackingBranch, pullTrackingBranch, StringComparison.Ordinal))
            {
                pushCounts = pullCounts;
            }
            else
            {
                pushCounts = await GetRevisionCountsAsync(
                    repository,
                    localBranch.Name,
                    pushTrackingBranch,
                    cancellationToken);
            }

            string pushTrackingState = CreateTrackingState(
                isPublishedToPushRemote,
                pushCounts.AheadCount,
                pushCounts.BehindCount);

            branches.Add(new BranchSynchronizationItem(
                localBranch.Name,
                localBranch.IsCurrent,
                localBranch.UpstreamBranch,
                localBranch.UpstreamRemoteName,
                pullTrackingState,
                selection.PushRemoteName,
                selection.ExplicitPushRemoteName,
                pushTrackingBranch,
                pushTrackingState,
                pushCounts.AheadCount,
                pushCounts.BehindCount,
                selection.HasPushRemoteOverride,
                pullTrackingBranch,
                string.Equals(
                    selection.PullRemoteName,
                    defaultRemote.Name,
                    StringComparison.Ordinal),
                isPublishedToPullRemote,
                pullCounts.AheadCount,
                pullCounts.BehindCount,
                localBranch.WorktreePath));
        }

        return branches
            .OrderByDescending(branch => branch.IsCurrent)
            .ThenBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlySet<string>> GetSynchronizationRemoteNamesAsync(
        RepositoryInfo repository,
        GitRemote defaultRemote,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BranchRemoteSelection> remoteSelections =
            await GetBranchRemoteSelectionsAsync(repository, defaultRemote, cancellationToken);
        HashSet<string> remoteNames = new(StringComparer.Ordinal)
        {
            defaultRemote.Name
        };

        foreach (BranchRemoteSelection selection in remoteSelections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selection.Branch.HasUpstream && selection.PullRemoteName != ".")
            {
                remoteNames.Add(selection.PullRemoteName);
            }

            if (!string.IsNullOrWhiteSpace(selection.PushRemoteName)
                && selection.PushRemoteName != ".")
            {
                remoteNames.Add(selection.PushRemoteName);
            }
        }

        return remoteNames;
    }

    private async Task<IReadOnlyList<BranchRemoteSelection>> GetBranchRemoteSelectionsAsync(
        RepositoryInfo repository,
        GitRemote defaultRemote,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LocalBranchReference> localBranches = await GetLocalBranchReferencesAsync(
            repository,
            cancellationToken);
        IReadOnlyDictionary<string, string> branchPushRemotes =
            await _gitConfigService.GetBranchPushRemotesAsync(repository);
        string pushDefaultRemote = await _gitConfigService.GetPushDefaultRemoteAsync(
            ConfigScope.None,
            repository);
        List<BranchRemoteSelection> selections = [];

        foreach (LocalBranchReference localBranch in localBranches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            selections.Add(CreateBranchRemoteSelection(
                localBranch,
                defaultRemote.Name,
                branchPushRemotes,
                pushDefaultRemote));
        }

        return selections;
    }

    private static BranchRemoteSelection CreateBranchRemoteSelection(
        LocalBranchReference localBranch,
        string defaultRemoteName,
        IReadOnlyDictionary<string, string> branchPushRemotes,
        string pushDefaultRemote)
    {
        bool hasUpstream = localBranch.HasUpstream;
        string pullRemoteName = hasUpstream
            ? localBranch.UpstreamRemoteName
            : defaultRemoteName;
        string pullTrackingBranch = hasUpstream
            ? localBranch.UpstreamBranch
            : CreateRemoteTrackingBranch(defaultRemoteName, localBranch.Name);

        branchPushRemotes.TryGetValue(localBranch.Name, out string? explicitPushRemoteName);
        string pushRemoteName = ResolvePushRemoteName(
            explicitPushRemoteName,
            pushDefaultRemote,
            hasUpstream ? localBranch.UpstreamRemoteName : "",
            defaultRemoteName);
        string pushTrackingBranch = IsTrackingBranchForRemote(
            localBranch.PushTrackingBranch,
            pushRemoteName)
            ? localBranch.PushTrackingBranch
            : CreateRemoteTrackingBranch(pushRemoteName, localBranch.Name);

        return new BranchRemoteSelection(
            localBranch,
            pullRemoteName,
            pullTrackingBranch,
            pushRemoteName,
            explicitPushRemoteName ?? "",
            pushTrackingBranch,
            !string.IsNullOrWhiteSpace(explicitPushRemoteName)
                || !string.IsNullOrWhiteSpace(pushDefaultRemote));
    }

    private async Task<GitRemoteBranchStatus?> CreateRemoteBranchStatusAsync(
        RepositoryInfo repository,
        string localBranchName,
        string remoteName,
        string remoteTrackingBranch,
        IReadOnlySet<string> remoteTrackingBranches,
        bool includeUnpublished,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(remoteName)
            || string.IsNullOrWhiteSpace(remoteTrackingBranch))
        {
            return null;
        }

        bool isPublished = remoteTrackingBranches.Contains(remoteTrackingBranch);
        if (!isPublished && !includeUnpublished)
        {
            return null;
        }

        (int AheadCount, int BehindCount) counts = isPublished
            ? await GetRevisionCountsAsync(
                repository,
                localBranchName,
                remoteTrackingBranch,
                cancellationToken)
            : (0, 0);
        return new GitRemoteBranchStatus(
            remoteName,
            remoteTrackingBranch,
            isPublished,
            counts.AheadCount,
            counts.BehindCount);
    }

    private static string CreateRemoteTrackingBranch(string remoteName, string branchName)
    {
        return string.IsNullOrWhiteSpace(remoteName)
            ? ""
            : $"{remoteName}/{branchName}";
    }

    private async Task<IReadOnlySet<string>> GetAllRemoteTrackingBranchesAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        GitRemoteOperationResult output = await RunGitAsync(
            repository,
            false,
            cancellationToken,
            "for-each-ref",
            "refs/remotes",
            "--format=%(refname:short)");

        return output.Output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(reference => reference.Trim())
            .Where(reference => !reference.EndsWith("/HEAD", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string ResolvePushRemoteName(
        string? explicitPushRemoteName,
        string pushDefaultRemote,
        string upstreamRemoteName,
        string defaultRemoteName)
    {
        if (!string.IsNullOrWhiteSpace(explicitPushRemoteName))
        {
            return explicitPushRemoteName;
        }

        if (!string.IsNullOrWhiteSpace(pushDefaultRemote))
        {
            return pushDefaultRemote;
        }

        return !string.IsNullOrWhiteSpace(upstreamRemoteName)
            ? upstreamRemoteName
            : defaultRemoteName;
    }

    private static bool IsTrackingBranchForRemote(string trackingBranch, string remoteName)
    {
        return !string.IsNullOrWhiteSpace(trackingBranch)
            && trackingBranch.StartsWith($"{remoteName}/", StringComparison.Ordinal);
    }

    private static string CreateTrackingState(bool isPublished, int aheadCount, int behindCount)
    {
        if (!isPublished)
        {
            return "";
        }

        return (aheadCount, behindCount) switch
        {
            (> 0, > 0) => "<>",
            (> 0, _) => ">",
            (_, > 0) => "<",
            _ => "="
        };
    }

    private async Task<(int AheadCount, int BehindCount)> GetRevisionCountsAsync(
        RepositoryInfo repository,
        string localBranch,
        string remoteTrackingBranch,
        CancellationToken cancellationToken)
    {
        GitRemoteOperationResult output = await RunGitAsync(
            repository,
            false,
            cancellationToken,
            "rev-list",
            "--left-right",
            "--count",
            $"refs/heads/{localBranch}...refs/remotes/{remoteTrackingBranch}");
        string[] parts = output.Output.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries);
        int aheadCount = parts.Length > 0 && int.TryParse(parts[0], out int ahead) ? ahead : 0;
        int behindCount = parts.Length > 1 && int.TryParse(parts[1], out int behind) ? behind : 0;
        return (aheadCount, behindCount);
    }

    private async Task<GitCommitPage> GetCommitsCoreAsync(
        RepositoryInfo repository,
        IReadOnlyList<string> revisionArguments,
        int skip,
        int count)
    {
        ValidatePageArguments(skip, count);
        int requestedCount = count + 1;
        List<string> arguments =
        [
            "log",
            $"--skip={skip}",
            $"--max-count={requestedCount}",
            "--date=iso-strict",
            $"--pretty=format:%H%x1f%h%x1f%an%x1f%ae%x1f%cn%x1f%ce%x1f%ad%x1f%s%x1f%B%x1f%P%x1e"
        ];
        arguments.AddRange(revisionArguments);
        Task<GitRemoteOperationResult> outputTask = RunGitAsync(
            repository,
            false,
            arguments.ToArray());
        Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> changedFilePathsTask =
            GetChangedFilePathsByCommitAsync(repository, revisionArguments, skip, requestedCount);
        await Task.WhenAll(outputTask, changedFilePathsTask);
        IReadOnlyList<GitCommit> commits = ParseCommits(
            (await outputTask).Output,
            await changedFilePathsTask);
        return CreateCommitPage(commits, count);
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetChangedFilePathsByCommitAsync(
        RepositoryInfo repository,
        IReadOnlyList<string> revisionArguments,
        int skip,
        int count)
    {
        List<string> arguments =
        [
            "log",
            $"--skip={skip}",
            $"--max-count={count}",
            "--name-only",
            "--pretty=format:%x1e%H"
        ];
        arguments.AddRange(revisionArguments);
        GitRemoteOperationResult output = await RunGitAsync(
            repository,
            false,
            arguments.ToArray());

        Dictionary<string, IReadOnlyList<string>> changedFilePaths =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (string record in output.Output.Split(
            RecordSeparator,
            StringSplitOptions.RemoveEmptyEntries))
        {
            List<string> lines = record
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
            if (lines.Count == 0)
            {
                continue;
            }

            changedFilePaths[lines[0]] = lines
                .Skip(1)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return changedFilePaths;
    }

    private static GitCommitPage CreateCommitPage(
        IReadOnlyList<GitCommit> commits,
        int count)
    {
        bool hasMore = commits.Count > count;
        return new GitCommitPage(commits.Take(count).ToList(), hasMore);
    }

    private async Task<GitRemoteOperationResult> RunGitAsync(
        RepositoryInfo repository,
        bool allowFailure,
        params string[] arguments)
    {
        return await RunGitAsync(repository, allowFailure, CancellationToken.None, arguments);
    }

    private async Task<GitRemoteOperationResult> RunGitAsync(
        RepositoryInfo repository,
        bool allowFailure,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        GitCommandResult result = await _commandRunner.RunAsync(
            repository.Path,
            arguments,
            new GitCommandOptions(ThrowOnError: false),
            cancellationToken);
        if (!result.IsSuccess)
        {
            if (allowFailure)
            {
                return new GitRemoteOperationResult("");
            }

            string output = result.CombinedOutput;
            throw new GitRemoteOperationException(
                string.IsNullOrWhiteSpace(output) ? "Git remote command failed." : output,
                result.ExitCode,
                ClassifyError(output));
        }

        return new GitRemoteOperationResult(result.CombinedOutput);
    }

    private static IReadOnlyList<GitRemote> ParseRemotes(string output)
    {
        var remotes = new Dictionary<string, (string FetchUrl, string PushUrl)>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            var name = parts[0];
            var url = parts[1];
            var kind = parts[2];
            remotes.TryGetValue(name, out var current);
            remotes[name] = kind.Contains("fetch", StringComparison.OrdinalIgnoreCase)
                ? (url, current.PushUrl)
                : (current.FetchUrl, url);
        }

        return remotes
            .Select(item => new GitRemote(item.Key, item.Value.FetchUrl, item.Value.PushUrl))
            .OrderBy(item => item.Name)
            .ToList();
    }

    private static IReadOnlyList<GitTag> ParseRemoteTags(string output, GitRemote remote)
    {
        Dictionary<string, (string ObjectHash, string PeeledObjectHash)> tags =
            new(StringComparer.Ordinal);
        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !parts[1].StartsWith("refs/tags/", StringComparison.Ordinal))
            {
                continue;
            }

            string tagReference = parts[1]["refs/tags/".Length..];
            bool isPeeledReference = tagReference.EndsWith("^{}", StringComparison.Ordinal);
            string tagName = isPeeledReference ? tagReference[..^3] : tagReference;
            tags.TryGetValue(tagName, out (string ObjectHash, string PeeledObjectHash) current);
            tags[tagName] = isPeeledReference
                ? (current.ObjectHash, parts[0])
                : (parts[0], current.PeeledObjectHash);
        }

        return tags
            .Select(tag => new GitTag(
                $"{remote.Name}/{tag.Key}",
                isRemote: true,
                isAnnotated: !string.IsNullOrWhiteSpace(tag.Value.PeeledObjectHash),
                string.IsNullOrWhiteSpace(tag.Value.PeeledObjectHash)
                    ? tag.Value.ObjectHash
                    : tag.Value.PeeledObjectHash,
                "",
                null,
                remote.Name,
                tag.Key,
                tag.Value.ObjectHash))
            .OrderBy(tag => tag.RemoteTagName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<GitCommit> ParseCommits(
        string output,
        IReadOnlyDictionary<string, IReadOnlyList<string>> changedFilePathsByCommit)
    {
        var commits = new List<GitCommit>();
        foreach (var record in output.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.Trim('\r', '\n').Split(UnitSeparator);
            if (fields.Length < 10)
            {
                continue;
            }

            changedFilePathsByCommit.TryGetValue(
                fields[0],
                out IReadOnlyList<string>? changedFilePaths);
            IReadOnlyList<string> parentHashes = fields[9]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            commits.Add(new GitCommit(
                fields[0],
                fields[1],
                fields[2],
                fields[3],
                ParseDate(fields[6]),
                fields[7],
                fields[8],
                changedFilePaths: changedFilePaths,
                parentHashes: parentHashes,
                committerName: fields[4],
                committerEmail: fields[5]));
        }

        return commits;
    }

    private static IReadOnlyList<GitCommit> ParseComparisonCommits(
        string output,
        string leftLabel,
        string rightLabel,
        IReadOnlyDictionary<string, IReadOnlyList<string>> changedFilePathsByCommit)
    {
        var commits = new List<GitCommit>();
        foreach (string record in output.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.Trim('\r', '\n').Split(UnitSeparator);
            if (fields.Length < 11)
            {
                continue;
            }

            bool isLeftSide = fields[0].Trim() == "<";
            string sideLabel = isLeftSide ? leftLabel : rightLabel;
            changedFilePathsByCommit.TryGetValue(
                fields[1],
                out IReadOnlyList<string>? changedFilePaths);
            IReadOnlyList<string> parentHashes = fields[10]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            commits.Add(new GitCommit(
                fields[1],
                fields[2],
                fields[3],
                fields[4],
                ParseDate(fields[7]),
                fields[8],
                fields[9],
                changedFilePaths: changedFilePaths,
                parentHashes: parentHashes,
                rangeSideLabel: sideLabel,
                rangeSide: isLeftSide ? GitCommitRangeSide.Left : GitCommitRangeSide.Right,
                committerName: fields[5],
                committerEmail: fields[6]));
        }

        return commits;
    }

    private static void ValidateRevision(string revision, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(revision) || revision.StartsWith('-'))
        {
            throw new ArgumentException("A valid Git revision is required.", parameterName);
        }
    }

    private static void ValidatePageArguments(int skip, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(count, 0);
        if (count == int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
    }

    private static IReadOnlyList<TagSynchronizationItem> CreateTagSynchronizationItems(
        IReadOnlyList<GitTag> localTags,
        IReadOnlyDictionary<string, (string ReferenceObjectHash, string ObjectHash)> remoteTags)
    {
        List<TagSynchronizationItem> tags = [];
        foreach (GitTag localTag in localTags)
        {
            remoteTags.TryGetValue(
                localTag.Name,
                out (string ReferenceObjectHash, string ObjectHash) remoteTag);
            tags.Add(new TagSynchronizationItem(
                localTag,
                remoteTag.ReferenceObjectHash ?? "",
                remoteTag.ObjectHash ?? ""));
        }

        return tags;
    }

    private static DateTimeOffset? ParseDate(string value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static GitRemoteOperationErrorKind ClassifyError(string output)
    {
        if (output.Contains("does not support --atomic push", StringComparison.OrdinalIgnoreCase)
            || output.Contains("does not support atomic push", StringComparison.OrdinalIgnoreCase))
        {
            return GitRemoteOperationErrorKind.AtomicNotSupported;
        }

        if (output.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase)
            || output.Contains("could not read Username", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
            || output.Contains("403", StringComparison.OrdinalIgnoreCase)
            || output.Contains("401", StringComparison.OrdinalIgnoreCase))
        {
            return GitRemoteOperationErrorKind.Authentication;
        }

        if (output.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Automatic merge failed", StringComparison.OrdinalIgnoreCase)
            || output.Contains("fix conflicts", StringComparison.OrdinalIgnoreCase))
        {
            return GitRemoteOperationErrorKind.Conflict;
        }

        if (output.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase)
            || output.Contains("fetch first", StringComparison.OrdinalIgnoreCase)
            || output.Contains("rejected", StringComparison.OrdinalIgnoreCase))
        {
            return GitRemoteOperationErrorKind.NonFastForward;
        }

        return GitRemoteOperationErrorKind.General;
    }

    private static void ValidateReferenceName(string referenceName, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(referenceName)
            || referenceName.Any(char.IsWhiteSpace)
            || referenceName.Contains("..", StringComparison.Ordinal)
            || referenceName.Contains("@{", StringComparison.Ordinal)
            || referenceName.IndexOfAny(['~', '^', ':', '?', '*', '[', '\\']) >= 0
            || referenceName.StartsWith('.')
            || referenceName.EndsWith('.')
            || referenceName.StartsWith('/')
            || referenceName.EndsWith('/')
            || referenceName.Contains("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("A valid Git reference name is required.", parameterName);
        }
    }

    private static string CombineOutput(string output, string error)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return error.Trim();
        }

        if (string.IsNullOrWhiteSpace(error))
        {
            return output.Trim();
        }

        return $"{output.Trim()}{Environment.NewLine}{error.Trim()}";
    }

    private sealed record LocalBranchReference(
        string Name,
        bool IsCurrent,
        string UpstreamBranch,
        string UpstreamRemoteName,
        string UpstreamTrackingState,
        string PushRemoteName,
        string PushTrackingBranch,
        string PushTrackingState,
        string WorktreePath)
    {
        public bool HasUpstream => !string.IsNullOrWhiteSpace(UpstreamBranch)
            && !string.IsNullOrWhiteSpace(UpstreamRemoteName);
    }

    private sealed record BranchRemoteSelection(
        LocalBranchReference Branch,
        string PullRemoteName,
        string PullTrackingBranch,
        string PushRemoteName,
        string ExplicitPushRemoteName,
        string PushTrackingBranch,
        bool HasPushRemoteOverride);
}
