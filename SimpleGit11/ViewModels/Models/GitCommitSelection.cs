using System.Collections.Generic;
using System.Linq;
using SimpleGit11.Models;

namespace SimpleGit11.ViewModels;

internal static class GitCommitSelection
{
    public static IReadOnlyList<GitCommit> OrderOldestFirst(
        IReadOnlyList<GitCommit> historyNewestFirst,
        IReadOnlyCollection<GitCommit> selectedCommits)
    {
        HashSet<string> selectedHashes = selectedCommits
            .Select(commit => commit.Hash)
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        return historyNewestFirst
            .Where(commit => selectedHashes.Contains(commit.Hash))
            .Reverse()
            .ToArray();
    }

    public static bool IsWithinScope(
        CommitRangeCherryPickScope scope,
        IReadOnlyCollection<GitCommit> selectedCommits)
    {
        return scope switch
        {
            CommitRangeCherryPickScope.AllCommits => true,
            CommitRangeCherryPickScope.RightSide =>
                selectedCommits.All(commit => commit.RangeSide == GitCommitRangeSide.Right),
            _ => false
        };
    }

    public static bool CanApplyTogether(IReadOnlyCollection<GitCommit> selectedCommits)
    {
        return selectedCommits.Count == 1 || selectedCommits.All(commit => !commit.IsMerge);
    }
}
