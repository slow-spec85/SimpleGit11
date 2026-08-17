using System;

namespace SimpleGit11.Models;

public sealed class TagSynchronizationItem(
    GitTag localTag,
    string remoteReferenceObjectHash,
    string remoteObjectHash)
{
    public GitTag LocalTag { get; } = localTag;

    public string Name => LocalTag.Name;

    public string RemoteObjectHash { get; } = remoteObjectHash;

    public string RemoteReferenceObjectHash { get; } = remoteReferenceObjectHash;

    public bool IsPublishedToRemote => !string.IsNullOrWhiteSpace(RemoteReferenceObjectHash);

    public bool HasConflict => IsPublishedToRemote
        && !LocalTag.ReferenceObjectHash.Equals(RemoteReferenceObjectHash, StringComparison.OrdinalIgnoreCase);

    public bool NeedsPublishing => !IsPublishedToRemote;

    public bool NeedsSynchronization => NeedsPublishing || HasConflict;

    public bool CanPush => NeedsPublishing;
}
