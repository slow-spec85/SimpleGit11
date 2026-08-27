using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services;

public sealed class GitStatusService : IGitStatusService
{
    private readonly IGitCommandRunner _commandRunner;

    public GitStatusService(IGitCommandRunner? commandRunner = null)
    {
        _commandRunner = commandRunner ?? new GitCommandRunner();
    }

    public async Task<GitStatusSnapshot> GetStatusAsync(RepositoryInfo repository)
    {
        if (!Directory.Exists(repository.Path))
        {
            throw new DirectoryNotFoundException(repository.Path);
        }

        Task<string> statusTask = RunGitAsync(repository, "status", "--porcelain=v1");
        Task<string> indexTask = RunGitAsync(repository, "ls-files", "--stage", "-z");
        Task<IReadOnlyDictionary<string, DiffStat>> stagedStatsTask =
            GetDiffStatsAsync(repository, true);
        Task<IReadOnlyDictionary<string, DiffStat>> unstagedStatsTask =
            GetDiffStatsAsync(repository, false);
        await Task.WhenAll(statusTask, indexTask, stagedStatsTask, unstagedStatsTask);

        return ParseStatus(
            await statusTask,
            await stagedStatsTask,
            await unstagedStatsTask,
            ParseSubmodulePaths(await indexTask));
    }

    public async Task<GitOperationState> GetOperationStateAsync(RepositoryInfo repository)
    {
        string gitDirectory = (await RunGitAsync(repository, "rev-parse", "--git-dir")).Trim();
        if (!Path.IsPathRooted(gitDirectory))
        {
            gitDirectory = Path.GetFullPath(Path.Combine(repository.Path, gitDirectory));
        }

        if (Directory.Exists(Path.Combine(gitDirectory, "rebase-merge"))
            || File.Exists(Path.Combine(gitDirectory, "rebase-apply", "rebasing")))
        {
            return new GitOperationState(GitOperationKind.Rebase);
        }

        if (File.Exists(Path.Combine(gitDirectory, "MERGE_HEAD")))
        {
            string preparedMessage = await ReadPreparedCommitMessageAsync(gitDirectory);
            return new GitOperationState(GitOperationKind.Merge, preparedMessage);
        }

        if (File.Exists(Path.Combine(gitDirectory, "CHERRY_PICK_HEAD")))
        {
            string preparedMessage = await ReadPreparedCommitMessageAsync(gitDirectory);
            return new GitOperationState(GitOperationKind.CherryPick, preparedMessage);
        }

        if (File.Exists(Path.Combine(gitDirectory, "REVERT_HEAD")))
        {
            string preparedMessage = await ReadPreparedCommitMessageAsync(gitDirectory);
            return new GitOperationState(GitOperationKind.Revert, preparedMessage);
        }

        return GitOperationState.None;
    }

    private async Task<string> RunGitAsync(RepositoryInfo repository, params string[] arguments)
    {
        GitCommandResult result = await _commandRunner.RunAsync(repository.Path, arguments);
        return result.StandardOutput;
    }

    private async Task<IReadOnlyDictionary<string, DiffStat>> GetDiffStatsAsync(
        RepositoryInfo repository,
        bool staged)
    {
        var arguments = new List<string>
        {
            "diff",
            "--numstat",
            "--find-renames"
        };

        if (staged)
        {
            arguments.Add("--cached");
        }

        var output = await RunGitAsync(repository, arguments.ToArray());
        return ParseNumstat(output);
    }

    private static async Task<string> ReadPreparedCommitMessageAsync(string gitDirectory)
    {
        string messagePath = Path.Combine(gitDirectory, "MERGE_MSG");
        return File.Exists(messagePath)
            ? (await File.ReadAllTextAsync(messagePath)).Trim()
            : "";
    }

    private static GitStatusSnapshot ParseStatus(
        string output,
        IReadOnlyDictionary<string, DiffStat> stagedStats,
        IReadOnlyDictionary<string, DiffStat> unstagedStats,
        IReadOnlySet<string> submodulePaths)
    {
        var stagedChanges = new List<GitChangedFile>();
        var unstagedChanges = new List<GitChangedFile>();
        var conflictedChanges = new List<GitChangedFile>();

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length < 3)
            {
                continue;
            }

            var indexStatus = line[0];
            var workTreeStatus = line[1];
            var path = NormalizePath(line[3..]);

            if (IsConflict(indexStatus, workTreeStatus))
            {
                conflictedChanges.Add(new GitChangedFile(
                    path,
                    "Conflict",
                    GetStat(stagedStats, unstagedStats, path),
                    GitChangeState.Conflicted,
                    submodulePaths.Contains(path)));
                continue;
            }

            if (indexStatus != ' ' && indexStatus != '?')
            {
                stagedChanges.Add(new GitChangedFile(
                    path,
                    GetStatusName(indexStatus),
                    GetStat(stagedStats, path),
                    GitChangeState.Staged,
                    submodulePaths.Contains(path)));
            }

            if (workTreeStatus != ' ' || indexStatus == '?')
            {
                var status = indexStatus == '?' ? "Untracked" : GetStatusName(workTreeStatus);
                unstagedChanges.Add(new GitChangedFile(
                    path,
                    status,
                    GetStat(unstagedStats, path),
                    GitChangeState.Unstaged,
                    submodulePaths.Contains(path)));
            }
        }

        return new GitStatusSnapshot(stagedChanges, unstagedChanges, conflictedChanges);
    }

    private static IReadOnlySet<string> ParseSubmodulePaths(string output)
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (string entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int tabIndex = entry.IndexOf('\t');
            if (tabIndex <= 0)
            {
                continue;
            }

            string metadata = entry[..tabIndex];
            if (metadata.StartsWith("160000 ", StringComparison.Ordinal))
            {
                paths.Add(NormalizePath(entry[(tabIndex + 1)..]));
            }
        }

        return paths;
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

    private static DiffStat GetStat(
        IReadOnlyDictionary<string, DiffStat> stagedStats,
        IReadOnlyDictionary<string, DiffStat> unstagedStats,
        string path)
    {
        return DiffStat.Sum([GetStat(stagedStats, path), GetStat(unstagedStats, path)]);
    }

    private static int ParseCount(string value)
    {
        return int.TryParse(value, out var count) ? count : 0;
    }

    private static bool IsConflict(char indexStatus, char workTreeStatus)
    {
        if (indexStatus == 'U' || workTreeStatus == 'U')
        {
            return true;
        }

        return (indexStatus, workTreeStatus) is
            ('A', 'A') or
            ('D', 'D');
    }

    private static string NormalizePath(string path)
    {
        var normalizedPath = path.Trim().Trim('"');
        var numstatPath = NormalizeRenamePath(normalizedPath, " => ");
        if (numstatPath != normalizedPath)
        {
            return numstatPath;
        }

        const string renameSeparator = " -> ";

        var renameIndex = normalizedPath.IndexOf(renameSeparator, System.StringComparison.Ordinal);
        if (renameIndex >= 0)
        {
            return normalizedPath[(renameIndex + renameSeparator.Length)..].Trim().Trim('"');
        }

        return normalizedPath;
    }

    private static string NormalizeRenamePath(string path, string separator)
    {
        var renameIndex = path.IndexOf(separator, StringComparison.Ordinal);
        if (renameIndex < 0)
        {
            return path;
        }

        var openBraceIndex = path.LastIndexOf('{', renameIndex);
        var closeBraceIndex = path.IndexOf('}', renameIndex);
        if (openBraceIndex >= 0 && closeBraceIndex > renameIndex)
        {
            return (path[..openBraceIndex] + path[(renameIndex + separator.Length)..closeBraceIndex] + path[(closeBraceIndex + 1)..])
                .Trim()
                .Trim('"');
        }

        return path[(renameIndex + separator.Length)..].Trim().Trim('"');
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
            'U' => "Conflict",
            '?' => "Untracked",
            _ => "Changed"
        };
    }
}
