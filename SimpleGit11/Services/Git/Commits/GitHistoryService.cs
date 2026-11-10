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

    public Task<GitCommitPage> GetCommitsPageAsync(RepositoryInfo repository, int skip, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(count, 0);
        ArgumentOutOfRangeException.ThrowIfEqual(count, int.MaxValue);
        return ReadCommitsAsync(repository, skip, count);
    }

    public async Task<IReadOnlyList<GitCommit>> GetRemainingCommitsAsync(RepositoryInfo repository, int skip)
    {
        return (await ReadCommitsAsync(repository, skip, null)).Commits;
    }

    private async Task<GitCommitPage> ReadCommitsAsync(RepositoryInfo repository, int skip, int? count)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        int? requestedCount = count + 1;
        List<string> arguments = ["log", $"--skip={skip}", "--topo-order", "--date=iso-strict",
            "--pretty=format:%H%x1f%h%x1f%an%x1f%ae%x1f%cn%x1f%ce%x1f%ad%x1f%s%x1f%B%x1f%P%x1e"];
        if (requestedCount.HasValue) { arguments.Add($"--max-count={requestedCount.Value}"); }
        Task<string> logTask = RunGitAsync(repository, arguments.ToArray());
        Task<IReadOnlySet<string>> synchronizedHashesTask = GetSynchronizedCommitHashesAsync(repository);
        Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> changedFilePathsTask =
            GetChangedFilePathsByCommitAsync(repository, skip, requestedCount);
        Task<IReadOnlyDictionary<string, IReadOnlyList<GitCommitReference>>> referencesTask = GetReferencesByCommitAsync(repository);
        await Task.WhenAll(logTask, synchronizedHashesTask, changedFilePathsTask, referencesTask);
        IReadOnlyList<GitCommit> commits = ParseCommits(await logTask, await synchronizedHashesTask,
            await changedFilePathsTask, await referencesTask);
        return count.HasValue
            ? new GitCommitPage(commits.Take(count.Value).ToList(), commits.Count > count.Value)
            : new GitCommitPage(commits, false);
    }

    public async Task<GitCommit> GetLastCommitAsync(RepositoryInfo repository)
    {
        var output = await RunGitAsync(
            repository,
            "log",
            "-1",
            "--date=iso-strict",
            $"--pretty=format:%H%x1f%h%x1f%an%x1f%ae%x1f%cn%x1f%ce%x1f%ad%x1f%s%x1f%B%x1f%P%x1e");

        return ParseCommits(output).FirstOrDefault()
            ?? new GitCommit("", "", "", "", null, "", "");
    }


    public async Task<IReadOnlyList<GitChangedFile>> GetChangedFilesAsync(RepositoryInfo repository, GitCommit commit)
    {
        string? diffBase = commit.HasDiffBaseRevision ? commit.DiffBaseRevision
            : commit.IsMerge ? commit.ParentHashes[0] : null;
        string command = diffBase is null ? "show" : "diff";
        string[] revisionArguments = diffBase is null
            ? ["--format=", commit.Hash]
            : [diffBase, commit.Hash];
        string[] nameStatusArguments = [command, "--name-status", "-z", "--find-renames", .. revisionArguments];
        string[] numstatArguments = [command, "--numstat", "-z", "--find-renames", .. revisionArguments];
        string[] rawArguments = [command, "--raw", "-z", "--find-renames", .. revisionArguments];
        Task<string> nameStatusTask = RunGitAsync(repository, nameStatusArguments);
        Task<string> numstatTask = RunGitAsync(repository, numstatArguments);
        Task<string> rawTask = RunGitAsync(repository, rawArguments);
        await Task.WhenAll(nameStatusTask, numstatTask, rawTask);

        return ParseChangedFiles(
            await nameStatusTask,
            GitDiffParser.ParseNumstat(await numstatTask),
            ParseSubmodulePaths(await rawTask));
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
            if (fields.Length < 10)
            {
                continue;
            }

            IReadOnlyList<string>? changedFilePaths = null;
            changedFilePathsByCommit?.TryGetValue(fields[0], out changedFilePaths);

            IReadOnlyList<GitCommitReference>? references = null;
            referencesByCommit?.TryGetValue(fields[0], out references);

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
                synchronizedHashes is not null && synchronizedHashes.Contains(fields[0]),
                changedFilePaths,
                references,
                parentHashes,
                committerName: fields[4],
                committerEmail: fields[5]));
        }

        return commits;
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetChangedFilePathsByCommitAsync(
        RepositoryInfo repository,
        int skip,
        int? count)
    {
        List<string> arguments = ["log", $"--skip={skip}", "--topo-order", "--name-only", "--pretty=format:%x1e%H"];
        if (count.HasValue) { arguments.Add($"--max-count={count.Value}"); }
        string output = await RunGitAsync(repository, arguments.ToArray());

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
        IReadOnlyDictionary<string, DiffStat> stats,
        IReadOnlySet<string> submodulePaths)
    {
        List<GitChangedFile> files = [];
        string[] fields = output.Split('\0');
        for (int index = 0; index + 1 < fields.Length; index++)
        {
            string status = fields[index].TrimStart('\r', '\n');
            if (status.Length == 0) { continue; }
            string path = fields[++index];
            string? previousPath = null;
            if (status[0] is 'R' or 'C' && index + 1 < fields.Length)
            {
                previousPath = path;
                path = fields[++index];
            }

            files.Add(new GitChangedFile(
                path,
                GetStatusName(status[0]),
                GetStat(stats, path),
                isSubmodule: submodulePaths.Contains(path),
                previousPath: previousPath));
        }

        return files;
    }

    private static IReadOnlySet<string> ParseSubmodulePaths(string output)
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        string[] fields = output.Split('\0');
        for (int index = 0; index + 1 < fields.Length; index++)
        {
            string line = fields[index].TrimStart('\r', '\n');
            if (!line.StartsWith(':')) { continue; }
            string[] metadata = line[1..].Split(' ');
            if (metadata.Length != 5) { continue; }
            string path = fields[++index];
            if (metadata[4].Length > 0 && (metadata[4][0] is 'R' or 'C') && index + 1 < fields.Length)
            {
                path = fields[++index];
            }

            if (metadata[0] == "160000" || metadata[1] == "160000") { paths.Add(path); }
        }

        return paths;
    }

    private static DiffStat GetStat(IReadOnlyDictionary<string, DiffStat> stats, string path)
    {
        return stats.TryGetValue(path, out var stat) ? stat : DiffStat.Empty;
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
