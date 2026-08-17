using System;
using System.Collections.Generic;
using System.IO;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

internal static class GitWorktreeParser
{
    public static IReadOnlyList<GitWorktree> Parse(string output, RepositoryInfo repository)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(repository);

        List<GitWorktree> worktrees = [];
        WorktreeBuilder? current = null;

        foreach (string field in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (field.StartsWith("worktree ", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    worktrees.Add(current.Build(repository, worktrees.Count == 0));
                }

                current = new WorktreeBuilder { Path = field[9..] };
                continue;
            }

            current?.Apply(field);
        }

        if (current is not null)
        {
            worktrees.Add(current.Build(repository, worktrees.Count == 0));
        }

        return worktrees;
    }

    private sealed class WorktreeBuilder
    {
        public string Path { get; init; } = "";

        public string HeadHash { get; private set; } = "";

        public string BranchName { get; private set; } = "";

        public bool IsBare { get; private set; }

        public bool IsDetached { get; private set; }

        public bool IsLocked { get; private set; }

        public bool IsPrunable { get; private set; }

        public string LockReason { get; private set; } = "";

        public string PrunableReason { get; private set; } = "";

        public void Apply(string field)
        {
            if (field.StartsWith("HEAD ", StringComparison.Ordinal))
            {
                HeadHash = field[5..];
            }
            else if (field.StartsWith("branch refs/heads/", StringComparison.Ordinal))
            {
                BranchName = field[18..];
            }
            else if (field == "bare")
            {
                IsBare = true;
            }
            else if (field == "detached")
            {
                IsDetached = true;
            }
            else if (field.StartsWith("locked", StringComparison.Ordinal))
            {
                IsLocked = true;
                LockReason = field.Length > 7 ? field[7..].TrimStart() : "";
            }
            else if (field.StartsWith("prunable", StringComparison.Ordinal))
            {
                IsPrunable = true;
                PrunableReason = field.Length > 9 ? field[9..].TrimStart() : "";
            }
        }

        public GitWorktree Build(RepositoryInfo repository, bool isFirst)
        {
            bool isMain = !IsBare && (PathsEqual(Path, repository.MainWorktreePath) || isFirst);
            return new GitWorktree(
                Path,
                HeadHash,
                BranchName,
                IsBare,
                IsDetached,
                IsLocked,
                IsPrunable,
                isMain,
                PathsEqual(Path, repository.Path),
                LockReason,
                PrunableReason);
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(left)),
                System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
