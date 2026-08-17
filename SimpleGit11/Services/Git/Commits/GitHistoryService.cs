using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services;

public sealed class GitHistoryService : IGitHistoryService
{
    private const int DefaultCommitCount = 300;
    private const char RecordSeparator = '\x1e';
    private const char UnitSeparator = '\x1f';
    private readonly IGitCommandRunner _commandRunner;

    public GitHistoryService(IGitCommandRunner? commandRunner = null)
    {
        _commandRunner = commandRunner ?? new GitCommandRunner();
    }

    public async Task<IReadOnlyList<GitCommit>> GetCommitsAsync(RepositoryInfo repository)
    {
        GitCommitPage page = await GetCommitsPageAsync(repository, 0, DefaultCommitCount);
        return page.Commits;
    }

    public async Task<GitCommitPage> GetCommitsPageAsync(
        RepositoryInfo repository,
        int skip,
        int count)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(count, 0);
        if (count == int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        int requestedCount = count + 1;
        string output = await RunGitAsync(
            repository,
            "log",
            $"--skip={skip}",
            $"--max-count={requestedCount}",
            "--date=iso-strict",
            $"--pretty=format:%H%x1f%h%x1f%an%x1f%ae%x1f%ad%x1f%s%x1f%B%x1f%P%x1e");

        Task<IReadOnlySet<string>> synchronizedHashesTask = GetSynchronizedCommitHashesAsync(repository);
        Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> changedFilePathsTask =
            GetChangedFilePathsByCommitAsync(repository, skip, requestedCount);
        Task<IReadOnlyDictionary<string, IReadOnlyList<GitCommitReference>>> referencesTask =
            GetReferencesByCommitAsync(repository);

        await Task.WhenAll(synchronizedHashesTask, changedFilePathsTask, referencesTask);
        IReadOnlyList<GitCommit> commits = ParseCommits(
            output,
            await synchronizedHashesTask,
            await changedFilePathsTask,
            await referencesTask);
        bool hasMore = commits.Count > count;
        return new GitCommitPage(commits.Take(count).ToList(), hasMore);
    }

    public async Task<GitCommit> GetLastCommitAsync(RepositoryInfo repository)
    {
        var output = await RunGitAsync(
            repository,
            "log",
            "-1",
            "--date=iso-strict",
            $"--pretty=format:%H%x1f%h%x1f%an%x1f%ae%x1f%ad%x1f%s%x1f%B%x1f%P%x1e");

        return ParseCommits(output).FirstOrDefault()
            ?? new GitCommit("", "", "", "", null, "", "");
    }


    public async Task<IReadOnlyList<GitChangedFile>> GetChangedFilesAsync(RepositoryInfo repository, GitCommit commit)
    {
        string command = commit.HasDiffBaseRevision ? "diff" : "show";
        string[] revisionArguments = commit.HasDiffBaseRevision
            ? [commit.DiffBaseRevision, commit.Hash]
            : ["--format=", commit.Hash];
        string[] nameStatusArguments = [command, "--name-status", "--find-renames", .. revisionArguments];
        string[] numstatArguments = [command, "--numstat", "--find-renames", .. revisionArguments];
        var nameStatusOutput = await RunGitAsync(repository, nameStatusArguments);
        var numstatOutput = await RunGitAsync(repository, numstatArguments);

        return ParseChangedFiles(nameStatusOutput, ParseNumstat(numstatOutput));
    }

    public async Task<bool> HasLocalCommits(RepositoryInfo repository)
    {
        var output = await RunGitAsync(repository, "log", "--branches", "--not", "--remotes", "--oneline");
        return !string.IsNullOrEmpty(output);
    }

    private async Task<string> RunGitAsync(RepositoryInfo repository, params string[] arguments)
    {
        GitCommandResult result = await _commandRunner.RunAsync(
            repository.Path,
            arguments,
            new GitCommandOptions(ThrowOnError: false));
        if (!result.IsSuccess)
        {
            if (result.StandardError.Contains(
                "does not have any commits yet",
                StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            throw new GitCommandException(result.StandardError.Trim(), result.ExitCode);
        }

        return result.StandardOutput;
    }

    private static IReadOnlyList<GitCommit> ParseCommits(string output)
    {
        return ParseCommits(output, null);
    }

    private static IReadOnlyList<GitCommit> ParseCommits(
        string output,
        IReadOnlySet<string>? synchronizedHashes,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? changedFilePathsByCommit = null,
        IReadOnlyDictionary<string, IReadOnlyList<GitCommitReference>>? referencesByCommit = null)
    {
        var commits = new List<GitCommit>();
        foreach (var record in output.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.Trim('\r', '\n').Split(UnitSeparator);
            if (fields.Length < 8)
            {
                continue;
            }

            IReadOnlyList<string>? changedFilePaths = null;
            changedFilePathsByCommit?.TryGetValue(fields[0], out changedFilePaths);

            IReadOnlyList<GitCommitReference>? references = null;
            referencesByCommit?.TryGetValue(fields[0], out references);

            IReadOnlyList<string> parentHashes = fields[7]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            commits.Add(new GitCommit(
                fields[0],
                fields[1],
                fields[2],
                fields[3],
                ParseDate(fields[4]),
                fields[5],
                fields[6],
                synchronizedHashes is not null && synchronizedHashes.Contains(fields[0]),
                changedFilePaths,
                references,
                parentHashes));
        }

        return commits;
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetChangedFilePathsByCommitAsync(
        RepositoryInfo repository,
        int skip,
        int count)
    {
        string output = await RunGitAsync(
            repository,
            "log",
            $"--skip={skip}",
            $"--max-count={count}",
            "--name-only",
            $"--pretty=format:%x1e%H");

        var changedFilePaths = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in output.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = record
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (lines.Count == 0)
            {
                continue;
            }

            var hash = lines[0];
            var paths = lines
                .Skip(1)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            changedFilePaths[hash] = paths;
        }

        return changedFilePaths;
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<GitCommitReference>>> GetReferencesByCommitAsync(
        RepositoryInfo repository)
    {
        var output = await RunGitAsync(
            repository,
            "for-each-ref",
            "--format=%(objectname)%00%(*objectname)%00%(refname)%00%(symref)",
            "refs/heads",
            "refs/remotes",
            "refs/tags");

        Dictionary<string, List<GitCommitReference>> mutableReferences = new(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] fields = line.Split('\0');
            if (fields.Length < 4 || !string.IsNullOrWhiteSpace(fields[3]))
            {
                continue;
            }

            if (!TryCreateCommitReference(fields[2], out GitCommitReference? reference))
            {
                continue;
            }

            string commitHash = string.IsNullOrWhiteSpace(fields[1]) ? fields[0] : fields[1];
            if (!mutableReferences.TryGetValue(commitHash, out List<GitCommitReference>? references))
            {
                references = [];
                mutableReferences[commitHash] = references;
            }

            references.Add(reference);
        }

        return mutableReferences.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<GitCommitReference>)item.Value
                .OrderBy(reference => reference.Kind)
                .ThenBy(reference => reference.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryCreateCommitReference(
        string fullName,
        [NotNullWhen(true)] out GitCommitReference? reference)
    {
        const string LocalBranchPrefix = "refs/heads/";
        const string RemoteBranchPrefix = "refs/remotes/";
        const string TagPrefix = "refs/tags/";

        if (fullName.StartsWith(LocalBranchPrefix, StringComparison.Ordinal))
        {
            reference = new GitCommitReference(
                fullName[LocalBranchPrefix.Length..],
                GitCommitReferenceKind.LocalBranch);
            return true;
        }

        if (fullName.StartsWith(RemoteBranchPrefix, StringComparison.Ordinal))
        {
            reference = new GitCommitReference(
                fullName[RemoteBranchPrefix.Length..],
                GitCommitReferenceKind.RemoteBranch);
            return true;
        }

        if (fullName.StartsWith(TagPrefix, StringComparison.Ordinal))
        {
            reference = new GitCommitReference(
                fullName[TagPrefix.Length..],
                GitCommitReferenceKind.Tag);
            return true;
        }

        reference = null;
        return false;
    }

    private async Task<IReadOnlySet<string>> GetSynchronizedCommitHashesAsync(RepositoryInfo repository)
    {
        var output = await RunGitAsync(repository, "rev-list", "--remotes");
        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? ParseDate(string value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static IReadOnlyList<GitChangedFile> ParseChangedFiles(
        string output,
        IReadOnlyDictionary<string, DiffStat> stats)
    {
        var files = new List<GitChangedFile>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var status = GetStatusName(parts[0][0]);
            var path = parts[0][0] is 'R' or 'C' && parts.Length >= 3
                ? parts[2]
                : parts[1];

            path = path.Trim().Trim('"');
            files.Add(new GitChangedFile(path, status, GetStat(stats, path)));
        }

        return files;
    }

    private static IReadOnlyDictionary<string, DiffStat> ParseNumstat(string output)
    {
        var stats = new Dictionary<string, DiffStat>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            var path = NormalizePath(parts.Last());
            stats[path] = new DiffStat(ParseCount(parts[0]), ParseCount(parts[1]));
        }

        return stats;
    }

    private static DiffStat GetStat(IReadOnlyDictionary<string, DiffStat> stats, string path)
    {
        return stats.TryGetValue(path, out var stat) ? stat : DiffStat.Empty;
    }

    private static int ParseCount(string value)
    {
        return int.TryParse(value, out var count) ? count : 0;
    }

    private static string NormalizePath(string path)
    {
        var normalizedPath = path.Trim().Trim('"');
        const string renameSeparator = " => ";

        var renameIndex = normalizedPath.IndexOf(renameSeparator, StringComparison.Ordinal);
        if (renameIndex >= 0)
        {
            var openBraceIndex = normalizedPath.LastIndexOf('{', renameIndex);
            var closeBraceIndex = normalizedPath.IndexOf('}', renameIndex);
            if (openBraceIndex >= 0 && closeBraceIndex > renameIndex)
            {
                return (normalizedPath[..openBraceIndex] + normalizedPath[(renameIndex + renameSeparator.Length)..closeBraceIndex] + normalizedPath[(closeBraceIndex + 1)..])
                    .Trim()
                    .Trim('"');
            }

            return normalizedPath[(renameIndex + renameSeparator.Length)..].Trim().Trim('"');
        }

        return normalizedPath;
    }

    private static string GetStatusName(char status)
    {
        return status switch
        {
            'A' => "Added",
            'D' => "Deleted",
            'M' => "Modified",
            'R' => "Renamed",
            'C' => "Copied",
            _ => "Changed"
        };
    }
}
