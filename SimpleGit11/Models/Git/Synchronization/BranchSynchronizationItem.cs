namespace SimpleGit11.Models;

public sealed class BranchSynchronizationItem(
    string name,
    bool isCurrent,
    string upstreamBranch,
    string upstreamRemoteName,
    string upstreamTrackingState,
    string pushRemoteName,
    string explicitPushRemoteName,
    string pushTrackingBranch,
    string pushTrackingState,
    int pushAheadCount,
    int pushBehindCount,
    bool hasPushRemoteOverride,
    string remoteTrackingBranch,
    bool tracksSelectedRemote,
    bool isPublishedToRemote,
    int aheadCount,
    int behindCount,
    string worktreePath = "")
{
    public string Name { get; } = name;

    public bool IsCurrent { get; } = isCurrent;

    public string WorktreePath { get; } = worktreePath;

    public bool IsInOtherWorktree => !IsCurrent && !string.IsNullOrWhiteSpace(WorktreePath);

    public string UpstreamBranch { get; } = upstreamBranch;

    public string UpstreamRemoteName { get; } = upstreamRemoteName;

    public string UpstreamTrackingState { get; } = upstreamTrackingState;

    public string PushRemoteName { get; } = pushRemoteName;

    public string ExplicitPushRemoteName { get; } = explicitPushRemoteName;

    public string PushTrackingBranch { get; } = pushTrackingBranch;

    public string PushTrackingState { get; } = pushTrackingState;

    public int PushAheadCount { get; } = pushAheadCount;

    public int PushBehindCount { get; } = pushBehindCount;

    public bool HasPushRemoteOverride { get; } = hasPushRemoteOverride;

    public string RemoteTrackingBranch { get; } = remoteTrackingBranch;

    public bool TracksSelectedRemote { get; } = tracksSelectedRemote;

    public bool IsPublishedToRemote { get; } = isPublishedToRemote;

    public int AheadCount { get; } = aheadCount;

    public int BehindCount { get; } = behindCount;

    public bool HasUpstream => !string.IsNullOrWhiteSpace(UpstreamBranch);

    public bool NeedsUpstream => !HasUpstream;

    public bool HasIncomingFromUpstream => UpstreamTrackingState is "<" or "<>";

    public bool HasIncomingFromPushRemote => PushBehindCount > 0;

    public bool RequiresForcePush => IsPublishedToPushRemote
        && PushAheadCount > 0
        && PushBehindCount > 0;

    public string ConfiguredPushRemoteName => !string.IsNullOrWhiteSpace(PushRemoteName)
        ? PushRemoteName
        : UpstreamRemoteName;

    public bool HasOutgoingToPushRemote => PushAheadCount > 0;

    public bool IsPublishedToPushRemote => !string.IsNullOrWhiteSpace(PushTrackingBranch)
        && !string.IsNullOrWhiteSpace(PushTrackingState);

    public bool NeedsPublishingToPushRemote => !IsPublishedToPushRemote;

    public bool HasOutgoingCommits => AheadCount > 0;

    public bool HasIncomingCommits => BehindCount > 0;

    public bool IsDiverged => HasOutgoingCommits && HasIncomingCommits;

    public bool NeedsPublishing => !IsPublishedToRemote;

    public bool HasOutgoingChanges => NeedsPublishing || HasOutgoingCommits;

    public bool NeedsSynchronization => CanPush || HasIncomingCommits;

    public bool CanPush => HasOutgoingToPushRemote || NeedsPublishingToPushRemote;

    public string OutgoingRevisionRange => IsPublishedToPushRemote
        ? $"{PushTrackingBranch}..{Name}"
        : "";

    public string IncomingRevisionRange => IsPublishedToRemote
        ? $"{Name}..{RemoteTrackingBranch}"
        : "";
}
