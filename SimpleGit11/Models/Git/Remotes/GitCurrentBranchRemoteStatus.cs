namespace SimpleGit11.Models;

public sealed class GitCurrentBranchRemoteStatus
{
    public GitCurrentBranchRemoteStatus(
        bool hasConfiguredUpstream,
        GitRemoteBranchStatus? trackingTarget,
        GitRemoteBranchStatus? pushTarget)
    {
        HasConfiguredUpstream = hasConfiguredUpstream;
        TrackingTarget = trackingTarget;
        PushTarget = pushTarget;
    }

    public bool HasConfiguredUpstream { get; }

    public GitRemoteBranchStatus? TrackingTarget { get; }

    public GitRemoteBranchStatus? PushTarget { get; }
}

public sealed class GitRemoteBranchStatus
{
    public GitRemoteBranchStatus(
        string remoteName,
        string trackingBranch,
        bool isPublished,
        int aheadCount,
        int behindCount)
    {
        RemoteName = remoteName;
        TrackingBranch = trackingBranch;
        IsPublished = isPublished;
        AheadCount = aheadCount;
        BehindCount = behindCount;
    }

    public string RemoteName { get; }

    public string TrackingBranch { get; }

    public bool IsPublished { get; }

    public int AheadCount { get; }

    public int BehindCount { get; }
}
