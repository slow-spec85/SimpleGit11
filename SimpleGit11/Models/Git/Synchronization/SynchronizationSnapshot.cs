using System.Collections.Generic;
using System.Linq;

namespace SimpleGit11.Models;

public sealed class SynchronizationSnapshot
{
    public SynchronizationSnapshot(
        GitRemote remote,
        IReadOnlyList<BranchSynchronizationItem> branches,
        IReadOnlyList<TagSynchronizationItem> tags)
    {
        Remote = remote;
        Branches = branches;
        Tags = tags;
        OutgoingBranches = branches.Where(branch => branch.CanPush).ToList();
        IncomingBranches = branches.Where(branch => branch.HasIncomingCommits).ToList();
        TagsToPush = tags.Where(tag => tag.NeedsPublishing).ToList();
        ConflictingTags = tags.Where(tag => tag.HasConflict).ToList();
        CurrentBranch = branches.FirstOrDefault(branch => branch.IsCurrent);
    }

    public GitRemote Remote { get; }

    public IReadOnlyList<BranchSynchronizationItem> Branches { get; }

    public IReadOnlyList<TagSynchronizationItem> Tags { get; }

    public IReadOnlyList<BranchSynchronizationItem> OutgoingBranches { get; }

    public IReadOnlyList<BranchSynchronizationItem> IncomingBranches { get; }

    public IReadOnlyList<TagSynchronizationItem> TagsToPush { get; }

    public IReadOnlyList<TagSynchronizationItem> ConflictingTags { get; }

    public BranchSynchronizationItem? CurrentBranch { get; }

    public bool HasChanges => Branches.Any(branch => branch.NeedsSynchronization)
        || Tags.Any(tag => tag.NeedsSynchronization);

    public bool IsSynchronized => !HasChanges;
}
