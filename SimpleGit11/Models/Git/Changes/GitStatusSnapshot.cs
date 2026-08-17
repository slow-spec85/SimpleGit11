using System.Collections.Generic;

namespace SimpleGit11.Models;

public sealed class GitStatusSnapshot
{
    public GitStatusSnapshot(
        IReadOnlyList<GitChangedFile> stagedChanges,
        IReadOnlyList<GitChangedFile> unstagedChanges,
        IReadOnlyList<GitChangedFile> conflictedChanges)
    {
        StagedChanges = stagedChanges;
        UnstagedChanges = unstagedChanges;
        ConflictedChanges = conflictedChanges;
    }

    public IReadOnlyList<GitChangedFile> StagedChanges { get; }

    public IReadOnlyList<GitChangedFile> UnstagedChanges { get; }

    public IReadOnlyList<GitChangedFile> ConflictedChanges { get; }
}
