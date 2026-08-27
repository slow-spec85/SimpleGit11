using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services;

public sealed class GitSubmoduleService : IGitSubmoduleService
{
    private const int MaximumRecursionDepth = 32;
    private readonly IGitCommandRunner _commandRunner;

    public GitSubmoduleService(IGitCommandRunner? commandRunner = null)
    {
        _commandRunner = commandRunner ?? new GitCommandRunner();
    }

    public Task<IReadOnlyList<GitSubmodule>> GetSubmodulesAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        HashSet<string> visitedPaths = new(StringComparer.OrdinalIgnoreCase);
        return GetSubmodulesAsync(repository.Path, visitedPaths, 0, cancellationToken);
    }

    public Task AddAsync(
        RepositoryInfo repository,
        SubmoduleAddRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Url);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);

        List<string> arguments = ["submodule", "add"];
        if (!string.IsNullOrWhiteSpace(request.Branch))
        {
            arguments.Add("--branch");
            arguments.Add(request.Branch.Trim());
        }

        arguments.Add("--");
        arguments.Add(request.Url.Trim());
        arguments.Add(request.Path.Trim());
        return RunMutationAsync(repository.Path, arguments, cancellationToken);
    }

    public async Task<IReadOnlyList<GitSubmoduleReferenceChange>> GetReferenceChangesAsync(
        string repositoryPath,
        string? oldRevision,
        string newRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newRevision);

        IReadOnlyDictionary<string, string> oldReferences = string.IsNullOrWhiteSpace(oldRevision)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await GetReferenceMapAsync(repositoryPath, oldRevision, cancellationToken);
        IReadOnlyDictionary<string, string> newReferences = await GetReferenceMapAsync(
            repositoryPath,
            newRevision,
            cancellationToken);
        List<GitSubmoduleReferenceChange> changes = [];

        foreach (string path in oldReferences.Keys
            .Concat(newReferences.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string oldCommit = oldReferences.GetValueOrDefault(path, "");
            string newCommit = newReferences.GetValueOrDefault(path, "");
            if (string.Equals(oldCommit, newCommit, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            GitSubmoduleReferenceChangeKind kind = string.IsNullOrWhiteSpace(oldCommit)
                ? GitSubmoduleReferenceChangeKind.Added
                : string.IsNullOrWhiteSpace(newCommit)
                    ? GitSubmoduleReferenceChangeKind.Removed
                    : GitSubmoduleReferenceChangeKind.Updated;
            changes.Add(new GitSubmoduleReferenceChange(path, oldCommit, newCommit, kind));
        }

        return changes;
    }

    public async Task<IReadOnlyList<GitSubmoduleApplicationState>> GetApplicationStatesAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        IReadOnlyList<GitSubmodule> submodules = await GetSubmodulesAsync(repository, cancellationToken);
        List<GitSubmoduleApplicationState> states = [];
        AddApplicationStates(submodules, repository.Path, "", states);
        return states.Where(state => state.RequiresApplication).ToList();
    }

    public Task InitializeAsync(
        string repositoryPath,
        string? submodulePath = null,
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["submodule", "update", "--init"];
        AddRecursiveAndPath(arguments, submodulePath, recursive);
        return RunMutationAsync(repositoryPath, arguments, cancellationToken);
    }

    public Task CheckoutRecordedAsync(
        string repositoryPath,
        string? submodulePath = null,
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["submodule", "update", "--checkout"];
        AddRecursiveAndPath(arguments, submodulePath, recursive);
        return RunMutationAsync(repositoryPath, arguments, cancellationToken);
    }

    public Task UpdateFromRemoteAsync(
        string repositoryPath,
        string? submodulePath = null,
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["submodule", "update", "--remote", "--checkout"];
        AddRecursiveAndPath(arguments, submodulePath, recursive);
        return RunMutationAsync(repositoryPath, arguments, cancellationToken);
    }

    public Task SyncAsync(
        string repositoryPath,
        string? submodulePath = null,
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["submodule", "sync"];
        AddRecursiveAndPath(arguments, submodulePath, recursive);
        return RunMutationAsync(repositoryPath, arguments, cancellationToken);
    }

    public async Task ApplyPinnedAsync(
        string repositoryPath,
        string? submodulePath = null,
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        await SyncAsync(repositoryPath, submodulePath, recursive, cancellationToken);
        List<string> arguments = ["submodule", "update", "--init", "--checkout"];
        AddRecursiveAndPath(arguments, submodulePath, recursive);
        await RunMutationAsync(repositoryPath, arguments, cancellationToken);
    }

    public Task SetUrlAsync(
        string repositoryPath,
        string submodulePath,
        string url,
        CancellationToken cancellationToken = default)
    {
        ValidateOperationTarget(repositoryPath, submodulePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        return RunMutationAsync(
            repositoryPath,
            ["submodule", "set-url", "--", submodulePath, url.Trim()],
            cancellationToken);
    }

    public Task SetBranchAsync(
        string repositoryPath,
        string submodulePath,
        string branch,
        CancellationToken cancellationToken = default)
    {
        ValidateOperationTarget(repositoryPath, submodulePath);
        IReadOnlyList<string> arguments = string.IsNullOrWhiteSpace(branch)
            ? ["submodule", "set-branch", "--default", "--", submodulePath]
            : ["submodule", "set-branch", "--branch", branch.Trim(), "--", submodulePath];
        return RunMutationAsync(repositoryPath, arguments, cancellationToken);
    }

    public Task DeinitializeAsync(
        string repositoryPath,
        string submodulePath,
        CancellationToken cancellationToken = default)
    {
        ValidateOperationTarget(repositoryPath, submodulePath);
        return RunMutationAsync(
            repositoryPath,
            ["submodule", "deinit", "--", submodulePath],
            cancellationToken);
    }

    public async Task RemoveAsync(
        string repositoryPath,
        string submodulePath,
        CancellationToken cancellationToken = default)
    {
        ValidateOperationTarget(repositoryPath, submodulePath);
        await RunMutationAsync(
            repositoryPath,
            ["submodule", "deinit", "--", submodulePath],
            cancellationToken);
        await RunMutationAsync(
            repositoryPath,
            ["rm", "--", submodulePath],
            cancellationToken);
    }

    private async Task<IReadOnlyList<GitSubmodule>> GetSubmodulesAsync(
        string repositoryPath,
        HashSet<string> visitedPaths,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth >= MaximumRecursionDepth || !visitedPaths.Add(NormalizePath(repositoryPath)))
        {
            return [];
        }

        string configurationPath = System.IO.Path.Combine(repositoryPath, ".gitmodules");
        if (!File.Exists(configurationPath))
        {
            return [];
        }

        GitCommandResult configurationResult = await RunQueryAsync(
            repositoryPath,
            ["config", "--null", "--file", ".gitmodules", "--list"],
            cancellationToken);
        if (!configurationResult.IsSuccess)
        {
            throw new GitCommandException(configurationResult.CombinedOutput, configurationResult.ExitCode);
        }

        IReadOnlyList<GitSubmoduleConfiguration> configurations =
            GitSubmoduleConfigurationParser.Parse(configurationResult.StandardOutput);
        List<GitSubmodule> submodules = [];
        foreach (GitSubmoduleConfiguration configuration in configurations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            submodules.Add(await CreateSubmoduleAsync(
                repositoryPath,
                configuration,
                visitedPaths,
                depth,
                cancellationToken));
        }

        return submodules;
    }

    private async Task<GitSubmodule> CreateSubmoduleAsync(
        string repositoryPath,
        GitSubmoduleConfiguration configuration,
        HashSet<string> visitedPaths,
        int depth,
        CancellationToken cancellationToken)
    {
        string fullPath = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(repositoryPath, configuration.Path));
        if (!RepositoryPathGuard.IsPathInsideRepository(repositoryPath, fullPath))
        {
            throw new GitCommandException(
                $"Submodule path is outside the repository: {configuration.Path}",
                -1);
        }

        Task<GitCommandResult> headCommitTask = RunQueryAsync(
            repositoryPath,
            ["ls-tree", "-z", "HEAD", "--", configuration.Path],
            cancellationToken);
        Task<GitCommandResult> indexCommitTask = RunQueryAsync(
            repositoryPath,
            ["ls-files", "--stage", "-z", "--", configuration.Path],
            cancellationToken);
        await Task.WhenAll(headCommitTask, indexCommitTask);

        string headCommit = ParseTreeCommit((await headCommitTask).StandardOutput);
        GitSubmoduleIndexState indexState = ParseIndexState((await indexCommitTask).StandardOutput);
        bool isInitialized = IsGitWorkingTree(fullPath);
        string checkedOutCommit = "";
        bool hasTrackedChanges = false;
        bool hasUntrackedFiles = false;
        bool hasConflict = indexState.HasConflict;
        string errorMessage = "";
        IReadOnlyList<GitSubmodule> children = [];

        if (isInitialized)
        {
            Task<GitCommandResult> checkedOutCommitTask = RunQueryAsync(
                fullPath,
                ["rev-parse", "--verify", "HEAD"],
                cancellationToken);
            Task<GitCommandResult> statusTask = RunQueryAsync(
                fullPath,
                ["status", "--porcelain=v2", "-z", "--untracked-files=normal", "--ignore-submodules=none"],
                cancellationToken);
            await Task.WhenAll(checkedOutCommitTask, statusTask);

            GitCommandResult checkedOutCommitResult = await checkedOutCommitTask;
            GitCommandResult statusResult = await statusTask;
            if (checkedOutCommitResult.IsSuccess)
            {
                checkedOutCommit = checkedOutCommitResult.StandardOutput.Trim();
            }
            else
            {
                errorMessage = checkedOutCommitResult.CombinedOutput;
            }

            if (statusResult.IsSuccess)
            {
                ParseWorkingTreeState(
                    statusResult.StandardOutput,
                    out hasTrackedChanges,
                    out hasUntrackedFiles,
                    out bool hasWorkingTreeConflict);
                hasConflict |= hasWorkingTreeConflict;
            }
            else if (string.IsNullOrWhiteSpace(errorMessage))
            {
                errorMessage = statusResult.CombinedOutput;
            }

            children = await GetSubmodulesAsync(
                fullPath,
                visitedPaths,
                depth + 1,
                cancellationToken);
        }

        return new GitSubmodule(
            configuration.Name,
            configuration.Path,
            fullPath,
            configuration.Url,
            configuration.Branch,
            headCommit,
            indexState.Commit,
            checkedOutCommit,
            isInitialized,
            hasTrackedChanges,
            hasUntrackedFiles,
            hasConflict,
            errorMessage,
            children);
    }

    private Task<GitCommandResult> RunQueryAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        return _commandRunner.RunAsync(
            workingDirectory,
            arguments,
            new GitCommandOptions(ThrowOnError: false),
            cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, string>> GetReferenceMapAsync(
        string repositoryPath,
        string revision,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await RunQueryAsync(
            repositoryPath,
            ["ls-tree", "-r", "-z", "--full-tree", revision],
            cancellationToken);
        if (!result.IsSuccess)
        {
            throw new GitCommandException(result.CombinedOutput, result.ExitCode);
        }

        Dictionary<string, string> references = new(StringComparer.Ordinal);
        foreach (string entry in result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int tabIndex = entry.IndexOf('\t');
            if (tabIndex < 0)
            {
                continue;
            }

            string[] metadata = entry[..tabIndex].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length >= 3 && metadata[0] == "160000" && metadata[1] == "commit")
            {
                references[entry[(tabIndex + 1)..]] = metadata[2];
            }
        }

        return references;
    }

    private static void AddApplicationStates(
        IReadOnlyList<GitSubmodule> submodules,
        string ownerRepositoryPath,
        string parentDisplayPath,
        List<GitSubmoduleApplicationState> states)
    {
        foreach (GitSubmodule submodule in submodules)
        {
            string displayPath = CombineGitPath(parentDisplayPath, submodule.Path);
            if (!string.IsNullOrWhiteSpace(submodule.IndexCommit))
            {
                states.Add(new GitSubmoduleApplicationState(
                    displayPath,
                    ownerRepositoryPath,
                    submodule.Path,
                    submodule.IndexCommit,
                    submodule.CheckedOutCommit,
                    submodule.IsInitialized));
            }

            AddApplicationStates(
                submodule.Children,
                submodule.FullPath,
                displayPath,
                states);
        }
    }

    private static string CombineGitPath(string parentPath, string childPath)
    {
        string normalizedChildPath = childPath.Replace('\\', '/').Trim('/');
        return string.IsNullOrWhiteSpace(parentPath)
            ? normalizedChildPath
            : $"{parentPath.TrimEnd('/')}/{normalizedChildPath}";
    }

    private async Task RunMutationAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        GitCommandResult result = await _commandRunner.RunAsync(
            workingDirectory,
            arguments,
            cancellationToken: cancellationToken);
        if (!result.IsSuccess)
        {
            throw new GitCommandException(result.CombinedOutput, result.ExitCode);
        }
    }

    private static void AddRecursiveAndPath(
        List<string> arguments,
        string? submodulePath,
        bool recursive)
    {
        if (recursive)
        {
            arguments.Add("--recursive");
        }

        if (!string.IsNullOrWhiteSpace(submodulePath))
        {
            arguments.Add("--");
            arguments.Add(submodulePath);
        }
    }

    private static void ValidateOperationTarget(string repositoryPath, string submodulePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(submodulePath);
    }

    private static bool IsGitWorkingTree(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        string dotGitPath = System.IO.Path.Combine(path, ".git");
        return Directory.Exists(dotGitPath) || File.Exists(dotGitPath);
    }

    private static string ParseTreeCommit(string output)
    {
        string entry = output.Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        int tabIndex = entry.IndexOf('\t');
        string metadata = tabIndex >= 0 ? entry[..tabIndex] : entry;
        string[] fields = metadata.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length >= 3 && fields[0] == "160000" ? fields[2] : "";
    }

    private static GitSubmoduleIndexState ParseIndexState(string output)
    {
        string commit = "";
        bool hasConflict = false;

        foreach (string entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int tabIndex = entry.IndexOf('\t');
            string metadata = tabIndex >= 0 ? entry[..tabIndex] : entry;
            string[] fields = metadata.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 3 || fields[0] != "160000" || !int.TryParse(fields[2], out int stage))
            {
                continue;
            }

            if (stage == 0 || stage == 2 || string.IsNullOrWhiteSpace(commit))
            {
                commit = fields[1];
            }

            hasConflict |= stage != 0;
        }

        return new GitSubmoduleIndexState(commit, hasConflict);
    }

    private static void ParseWorkingTreeState(
        string output,
        out bool hasTrackedChanges,
        out bool hasUntrackedFiles,
        out bool hasConflict)
    {
        hasTrackedChanges = false;
        hasUntrackedFiles = false;
        hasConflict = false;

        foreach (string entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (entry.StartsWith("? ", StringComparison.Ordinal))
            {
                hasUntrackedFiles = true;
            }
            else if (entry.StartsWith("u ", StringComparison.Ordinal))
            {
                hasTrackedChanges = true;
                hasConflict = true;
            }
            else if (entry.StartsWith("1 ", StringComparison.Ordinal)
                || entry.StartsWith("2 ", StringComparison.Ordinal))
            {
                hasTrackedChanges = true;
            }
        }
    }

    private static string NormalizePath(string path)
    {
        return System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
    }

    private sealed record GitSubmoduleIndexState(string Commit, bool HasConflict);
}
