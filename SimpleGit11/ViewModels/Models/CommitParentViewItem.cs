using System;
using SimpleGit11.Models;

namespace SimpleGit11.ViewModels;

public sealed class CommitParentViewItem
{
    public CommitParentViewItem(
        string hash,
        string relationship,
        GitCommit? commit,
        string unavailableTitle,
        string parentCommitTitle,
        bool isMergedHistory)
    {
        Hash = hash;
        Relationship = relationship;
        Commit = commit;
        UnavailableTitle = unavailableTitle;
        ParentCommitTitle = parentCommitTitle;
        IsMergedHistory = isMergedHistory;
    }

    public string Hash { get; }

    public string Relationship { get; }

    public GitCommit? Commit { get; }

    public string UnavailableTitle { get; }

    public string ParentCommitTitle { get; }

    public bool IsMergedHistory { get; }

    public string ShortHash => Commit?.ShortHash ?? Hash[..Math.Min(8, Hash.Length)];

    public string Title => Commit?.Title ?? UnavailableTitle;

    public string Tooltip => $"{ParentCommitTitle}\n{Relationship}\n{Hash}\n{Title}";
}
