using System;
using System.Collections.Generic;

namespace SimpleGit11.Models;

public sealed class GitCommit
{
    public GitCommit(
        string hash,
        string shortHash,
        string authorName,
        string authorEmail,
        DateTimeOffset? authoredAt,
        string title,
        string message,
        bool? isSynchronized = null,
        IReadOnlyList<string>? changedFilePaths = null,
        IReadOnlyList<GitCommitReference>? references = null,
        IReadOnlyList<string>? parentHashes = null,
        string rangeSideLabel = "",
        string diffBaseRevision = "",
        GitCommitRangeSide rangeSide = GitCommitRangeSide.None,
        string? committerName = null,
        string? committerEmail = null)
    {
        Hash = hash;
        ShortHash = shortHash;
        AuthorName = authorName;
        AuthorEmail = authorEmail;
        CommitterName = committerName ?? authorName;
        CommitterEmail = committerEmail ?? authorEmail;
        AuthoredAt = authoredAt;
        Title = title;
        Message = message;
        IsSynchronized = isSynchronized;
        ChangedFilePaths = changedFilePaths ?? [];
        References = references ?? [];
        ParentHashes = parentHashes ?? [];
        RangeSideLabel = rangeSideLabel;
        RangeSide = rangeSide;
        DiffBaseRevision = diffBaseRevision;
    }

    public string Hash { get; }

    public string ShortHash { get; }

    public string AuthorName { get; }

    public string AuthorEmail { get; }

    public string CommitterName { get; }

    public string CommitterEmail { get; }

    public DateTimeOffset? AuthoredAt { get; }

    public string Title { get; }
    public string Message { get; }

    public bool? IsSynchronized { get; }

    public bool NeedsSynchronization => IsSynchronized == false;

    public IReadOnlyList<string> ParentHashes { get; }

    public int ParentCount => ParentHashes.Count;

    public bool IsMerge => ParentHashes.Count > 1;

    public IReadOnlyList<string> ChangedFilePaths { get; }

    public IReadOnlyList<GitCommitReference> References { get; }

    public string RangeSideLabel { get; }

    public GitCommitRangeSide RangeSide { get; }

    public bool HasRangeSideLabel => !string.IsNullOrWhiteSpace(RangeSideLabel);

    public string DiffBaseRevision { get; }

    public bool HasDiffBaseRevision => !string.IsNullOrWhiteSpace(DiffBaseRevision);

    public bool HasReferences => References.Count > 0;

    public string DisplaySummary => string.IsNullOrWhiteSpace(Message)
        ? ShortHash
        : $"{ShortHash} {Message}";

    public string DisplayAuthor => string.IsNullOrWhiteSpace(AuthorEmail)
        ? AuthorName
        : $"{AuthorName} <{AuthorEmail}>";

    public string DisplayCommitter => string.IsNullOrWhiteSpace(CommitterEmail)
        ? CommitterName
        : $"{CommitterName} <{CommitterEmail}>";

    public bool HasDistinctCommitter =>
        !string.Equals(AuthorName, CommitterName, StringComparison.Ordinal) ||
        !string.Equals(AuthorEmail, CommitterEmail, StringComparison.Ordinal);

    public string DisplayDate => AuthoredAt?.ToString("g") ?? "";
}
