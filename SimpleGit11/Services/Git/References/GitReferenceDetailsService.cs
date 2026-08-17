using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services;

public sealed class GitReferenceDetailsService : IGitReferenceDetailsService
{
    private const char RecordSeparator = '\x1e';
    private const char UnitSeparator = '\x1f';
    private readonly IGitWorktreeService _worktreeService;
    private readonly IGitCommandRunner _commandRunner;

    public GitReferenceDetailsService(
        IGitWorktreeService worktreeService,
        IGitCommandRunner? commandRunner = null)
    {
        _worktreeService = worktreeService;
        _commandRunner = commandRunner ?? new GitCommandRunner();
    }

    public Task<GitCommit> GetBranchCommitAsync(RepositoryInfo repository, GitBranch branch)
    {
        string reference = branch.IsRemote ? $"refs/remotes/{branch.Name}" : $"refs/heads/{branch.Name}";
        return GetCommitAsync(repository, reference);
    }

    public async Task<GitBranchDetails> GetBranchComparisonAsync(RepositoryInfo repository, GitBranch branch)
    {
        string reference = branch.IsRemote ? $"refs/remotes/{branch.Name}" : $"refs/heads/{branch.Name}";
        Task<string> countsTask = RunGitAsync(repository, "rev-list", "--left-right", "--count", $"HEAD...{reference}");
        Task<GitCommit?> mergeBaseTask = GetMergeBaseCommitAsync(repository, "HEAD", reference);
        Task<bool> mergedTask = IsAncestorAsync(repository, reference, "HEAD");
        Task<bool> fastForwardTask = IsAncestorAsync(repository, "HEAD", reference);
        Task<(int Files, int Added, int Removed)> diffTask = GetDiffStatAsync(repository, $"HEAD..{reference}");
        await Task.WhenAll(countsTask, mergeBaseTask, mergedTask, fastForwardTask, diffTask);

        string countsOutput = await countsTask;
        int[] counts = countsOutput.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out int count) ? count : 0)
            .ToArray();
        (int files, int added, int removed) = await diffTask;

        return new GitBranchDetails(
            counts.Length > 0 ? counts[0] : 0,
            counts.Length > 1 ? counts[1] : 0,
            await mergeBaseTask,
            await mergedTask,
            await fastForwardTask,
            files,
            new DiffStat(added, removed));
    }

    public async Task<IReadOnlyList<GitWorktree>> GetBranchWorktreesAsync(RepositoryInfo repository, GitBranch branch)
    {
        return (await _worktreeService.GetWorktreesAsync(repository))
            .Where(item => item.BranchName.Equals(branch.Name, StringComparison.Ordinal))
            .ToList();
    }

    public async Task<GitTagDetails> GetTagDetailsAsync(RepositoryInfo repository, GitTag tag)
    {
        if (tag.IsRemote)
        {
            try
            {
                GitCommit remoteCommit = await GetCommitAsync(repository, tag.ObjectHash);
                return new GitTagDetails(
                    remoteCommit,
                    "commit",
                    "",
                    "",
                    tag.CreatedDate?.ToString("g") ?? "",
                    tag.Subject);
            }
            catch (GitCommandException)
            {
                return new GitTagDetails(null, "", "", "", tag.CreatedDate?.ToString("g") ?? "", tag.Subject);
            }
        }

        string reference = $"refs/tags/{tag.Name}";
        string objectType;
        try
        {
            objectType = (await RunGitAsync(repository, "cat-file", "-t", $"{reference}^{{}}")).Trim();
        }
        catch (GitCommandException)
        {
            return new GitTagDetails(null, "", "", "", "", "");
        }

        string metadataOutput = await RunGitAsync(
            repository,
            "for-each-ref",
            reference,
            $"--format=%(taggername){UnitSeparator}%(taggeremail){UnitSeparator}%(taggerdate:iso-strict){UnitSeparator}%(contents)");
        string[] metadata = metadataOutput.TrimEnd('\r', '\n').Split(UnitSeparator, 4);
        string taggerName = metadata.Length > 0 ? metadata[0] : "";
        string taggerEmail = metadata.Length > 1 ? metadata[1].Trim().Trim('<', '>') : "";
        string taggerDate = metadata.Length > 2 ? metadata[2] : "";
        string message = metadata.Length > 3 ? metadata[3].Trim() : "";

        if (!objectType.Equals("commit", StringComparison.Ordinal))
        {
            return new GitTagDetails(null, objectType, taggerName, taggerEmail, taggerDate, message);
        }

        GitCommit commit = await GetCommitAsync(repository, $"{reference}^{{commit}}");
        return new GitTagDetails(commit, objectType, taggerName, taggerEmail, taggerDate, message);
    }

    public async Task<GitTagSignatureDetails> GetTagSignatureAsync(RepositoryInfo repository, GitTag tag)
    {
        if (!tag.IsAnnotated)
        {
            return new GitTagSignatureDetails(
                GitTagSignatureStatus.NotSigned,
                GitSignatureType.Unknown,
                "",
                "",
                "",
                "");
        }

        string reference = tag.IsRemote
            ? tag.ReferenceObjectHash
            : $"refs/tags/{tag.Name}";
        string tagObject = await RunGitAsync(repository, "cat-file", "tag", reference);
        GitSignatureType signatureType = GetSignatureType(tagObject);
        if (signatureType == GitSignatureType.Unknown)
        {
            return new GitTagSignatureDetails(
                GitTagSignatureStatus.NotSigned,
                signatureType,
                "",
                "",
                "",
                "");
        }

        GitCommandResult verification = await RunGitWithResultAsync(repository, "verify-tag", "--raw", reference);
        string verificationOutput = string.Join(
            Environment.NewLine,
            new[] { verification.StandardOutput, verification.StandardError }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        string signer = GetSignatureSigner(verificationOutput);
        string keyId = GetSignatureKeyId(verificationOutput);
        string fingerprint = GetGpgStatusValue(verificationOutput, "VALIDSIG").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        GitTagSignatureStatus status = GetSignatureStatus(verification.ExitCode, verificationOutput);
        return new GitTagSignatureDetails(
            status,
            signatureType,
            signer,
            keyId,
            fingerprint,
            GetVerificationDiagnostic(verificationOutput));
    }

    public async Task<IReadOnlyList<GitWorktree>> GetTagWorktreesAsync(RepositoryInfo repository, GitTag tag)
    {
        return (await _worktreeService.GetWorktreesAsync(repository))
            .Where(item => item.HeadHash.Equals(tag.ObjectHash, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<IReadOnlyList<GitReflogEntry>> GetBranchReflogAsync(
        RepositoryInfo repository,
        GitBranch branch)
    {
        if (branch.IsRemote)
        {
            return [];
        }

        string output = await RunGitAsync(
            repository,
            "reflog",
            "show",
            "--date=iso-strict",
            $"--format=%H{UnitSeparator}%gD{UnitSeparator}%gs{UnitSeparator}%gN{UnitSeparator}%gE",
            $"refs/heads/{branch.Name}");
        List<(string NewHash, DateTimeOffset? OccurredAt, string Subject, string ActorName, string ActorEmail)> parsedEntries = [];
        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split(UnitSeparator);
            if (fields.Length < 5)
            {
                continue;
            }

            parsedEntries.Add((
                fields[0],
                ParseReflogDate(fields[1]),
                fields[2],
                fields[3],
                fields[4]));
        }

        List<GitReflogEntry> entries = [];
        for (int index = 0; index < parsedEntries.Count; index++)
        {
            (string newHash, DateTimeOffset? occurredAt, string subject, string actorName, string actorEmail) = parsedEntries[index];
            string previousHash = index + 1 < parsedEntries.Count
                ? parsedEntries[index + 1].NewHash
                : "";
            entries.Add(new GitReflogEntry(
                newHash,
                previousHash,
                occurredAt,
                subject,
                actorName,
                actorEmail));
        }

        return entries;
    }

    public async Task<GitTagRelationDetails> GetTagRelationAsync(RepositoryInfo repository, GitTag tag)
    {
        string reference = tag.IsRemote
            ? tag.ObjectHash
            : $"refs/tags/{tag.Name}^{{commit}}";
        string targetCommit = (await RunGitAsync(repository, "rev-parse", "--verify", reference)).Trim();
        Task<string> countsTask = RunGitAsync(repository, "rev-list", "--left-right", "--count", $"HEAD...{targetCommit}");
        Task<GitCommit?> mergeBaseTask = GetMergeBaseCommitAsync(repository, "HEAD", targetCommit);
        Task<string> containingBranchesTask = RunGitAsync(
            repository,
            "for-each-ref",
            "--format=%(refname:short)",
            $"--contains={targetCommit}",
            "refs/heads");
        await Task.WhenAll(countsTask, mergeBaseTask, containingBranchesTask);

        int[] counts = (await countsTask).Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out int count) ? count : 0)
            .ToArray();
        IReadOnlyList<string> containingBranches = (await containingBranchesTask)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(branch => branch.Trim())
            .Where(branch => !string.IsNullOrWhiteSpace(branch))
            .ToList();

        return new GitTagRelationDetails(
            counts.Length > 0 ? counts[0] : 0,
            counts.Length > 1 ? counts[1] : 0,
            await mergeBaseTask,
            containingBranches);
    }

    private async Task<GitCommit> GetCommitAsync(RepositoryInfo repository, string reference)
    {
        string format = string.Join(UnitSeparator,
            "%H", "%h", "%an", "%ae", "%aI", "%s", "%B", "%P");
        string output = await RunGitAsync(repository, "show", "-s", $"--format={format}", reference);
        string[] fields = output.TrimEnd('\r', '\n').Split(UnitSeparator);
        if (fields.Length < 7)
        {
            throw new GitCommandException("Commit details could not be parsed.", -1);
        }

        DateTimeOffset? authoredAt = DateTimeOffset.TryParse(fields[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate)
            ? parsedDate
            : null;
        IReadOnlyList<string> parents = fields.Length > 7
            ? fields[7].Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : [];
        return new GitCommit(fields[0], fields[1], fields[2], fields[3], authoredAt, fields[5], fields[6].Trim(), parentHashes: parents);
    }

    private static DateTimeOffset? ParseReflogDate(string selector)
    {
        int startIndex = selector.LastIndexOf("@{", StringComparison.Ordinal);
        if (startIndex < 0 || !selector.EndsWith('}'))
        {
            return null;
        }

        string value = selector[(startIndex + 2)..^1];
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset parsedDate)
                ? parsedDate
                : null;
    }

    private static GitSignatureType GetSignatureType(string tagObject)
    {
        if (tagObject.Contains("-----BEGIN PGP SIGNATURE-----", StringComparison.Ordinal)
            || tagObject.Contains("-----BEGIN PGP MESSAGE-----", StringComparison.Ordinal))
        {
            return GitSignatureType.OpenPgp;
        }

        if (tagObject.Contains("-----BEGIN SSH SIGNATURE-----", StringComparison.Ordinal))
        {
            return GitSignatureType.Ssh;
        }

        return tagObject.Contains("-----BEGIN SIGNED MESSAGE-----", StringComparison.Ordinal)
            ? GitSignatureType.X509
            : GitSignatureType.Unknown;
    }

    private static GitTagSignatureStatus GetSignatureStatus(int exitCode, string output)
    {
        if (exitCode == 0)
        {
            return GitTagSignatureStatus.Valid;
        }

        if (output.Contains("NO_PUBKEY", StringComparison.OrdinalIgnoreCase)
            || output.Contains("no public key", StringComparison.OrdinalIgnoreCase))
        {
            return GitTagSignatureStatus.UnknownKey;
        }

        if (output.Contains("BADSIG", StringComparison.OrdinalIgnoreCase)
            || output.Contains("ERRSIG", StringComparison.OrdinalIgnoreCase)
            || output.Contains("EXPSIG", StringComparison.OrdinalIgnoreCase)
            || output.Contains("EXPKEYSIG", StringComparison.OrdinalIgnoreCase)
            || output.Contains("REVKEYSIG", StringComparison.OrdinalIgnoreCase)
            || output.Contains("bad signature", StringComparison.OrdinalIgnoreCase))
        {
            return GitTagSignatureStatus.Invalid;
        }

        return GitTagSignatureStatus.Unavailable;
    }

    private static string GetSignatureSigner(string output)
    {
        foreach (string statusName in new[] { "GOODSIG", "BADSIG", "EXPSIG", "EXPKEYSIG", "REVKEYSIG" })
        {
            string value = GetGpgStatusValue(output, statusName);
            int separatorIndex = value.IndexOf(' ');
            if (separatorIndex >= 0 && separatorIndex < value.Length - 1)
            {
                return value[(separatorIndex + 1)..].Trim();
            }
        }

        const string sshPrefix = "Good \"git\" signature for ";
        int sshStart = output.IndexOf(sshPrefix, StringComparison.OrdinalIgnoreCase);
        if (sshStart >= 0)
        {
            string value = output[(sshStart + sshPrefix.Length)..];
            int endIndex = value.IndexOf(" with ", StringComparison.OrdinalIgnoreCase);
            return (endIndex >= 0 ? value[..endIndex] : value.Split(['\r', '\n'])[0]).Trim();
        }

        return "";
    }

    private static string GetSignatureKeyId(string output)
    {
        foreach (string statusName in new[] { "GOODSIG", "BADSIG", "NO_PUBKEY", "ERRSIG", "EXPSIG", "EXPKEYSIG", "REVKEYSIG" })
        {
            string value = GetGpgStatusValue(output, statusName);
            string keyId = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (!string.IsNullOrWhiteSpace(keyId))
            {
                return keyId;
            }
        }

        int keyStart = output.IndexOf("SHA256:", StringComparison.OrdinalIgnoreCase);
        if (keyStart >= 0)
        {
            string value = output[keyStart..];
            return value.Split([' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        }

        return "";
    }

    private static string GetGpgStatusValue(string output, string statusName)
    {
        string prefix = $"[GNUPG:] {statusName} ";
        string? line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return line is null ? "" : line[prefix.Length..].Trim();
    }

    private static string GetVerificationDiagnostic(string output)
    {
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => !line.StartsWith("[GNUPG:]", StringComparison.Ordinal))
            ?? "";
    }

    private async Task<bool> IsAncestorAsync(RepositoryInfo repository, string ancestor, string descendant)
    {
        try
        {
            await RunGitAsync(repository, "merge-base", "--is-ancestor", ancestor, descendant);
            return true;
        }
        catch (GitCommandException exception) when (exception.ExitCode == 1)
        {
            return false;
        }
    }

    private async Task<GitCommit?> GetMergeBaseCommitAsync(
        RepositoryInfo repository,
        string leftReference,
        string rightReference)
    {
        try
        {
            string mergeBase = (await RunGitAsync(repository, "merge-base", leftReference, rightReference)).Trim();
            return string.IsNullOrWhiteSpace(mergeBase)
                ? null
                : await GetCommitAsync(repository, mergeBase);
        }
        catch (GitCommandException exception) when (exception.ExitCode == 1)
        {
            return null;
        }
    }

    private async Task<(int Files, int Added, int Removed)> GetDiffStatAsync(RepositoryInfo repository, string range)
    {
        string output = await RunGitAsync(repository, "diff", "--numstat", range);
        int files = 0;
        int added = 0;
        int removed = 0;
        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split('\t');
            if (fields.Length < 3)
            {
                continue;
            }

            files++;
            if (int.TryParse(fields[0], out int addedLines)) added += addedLines;
            if (int.TryParse(fields[1], out int removedLines)) removed += removedLines;
        }

        return (files, added, removed);
    }

    private async Task<string> RunGitAsync(RepositoryInfo repository, params string[] arguments)
    {
        GitCommandResult result = await RunGitWithResultAsync(repository, arguments);
        if (result.ExitCode != 0)
        {
            throw new GitCommandException(result.StandardError.Trim(), result.ExitCode);
        }

        return result.StandardOutput;
    }

    private Task<GitCommandResult> RunGitWithResultAsync(RepositoryInfo repository, params string[] arguments)
    {
        return _commandRunner.RunAsync(
            repository.Path,
            arguments,
            new GitCommandOptions(ThrowOnError: false));
    }
}
