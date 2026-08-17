using System;

namespace SimpleGit11.Models;

public sealed class GitTag(
    string name,
    bool isRemote,
    bool isAnnotated,
    string objectHash,
    string subject,
    DateTime? createdDate,
    string remoteName = "",
    string remoteTagName = "",
    string referenceObjectHash = "",
    bool isCurrent = false)
{
    public string Name { get; } = name;

    public bool IsRemote { get; } = isRemote;

    public bool IsLocal => !IsRemote;

    public string RemoteName { get; } = remoteName;

    public string RemoteTagName { get; } = string.IsNullOrWhiteSpace(remoteTagName) ? name : remoteTagName;

    public bool IsAnnotated { get; } = isAnnotated;

    public string ObjectHash { get; } = objectHash;

    public string ReferenceObjectHash { get; } = string.IsNullOrWhiteSpace(referenceObjectHash)
        ? objectHash
        : referenceObjectHash;

    public bool IsCurrent { get; } = isCurrent;

    public string ShortCommitHash => ObjectHash.Length > 8 ? ObjectHash[..8] : ObjectHash;

    public string Subject { get; } = subject;

    public DateTime? CreatedDate { get; } = createdDate;

    public string CommitMetadata => $"{CreatedDate?.ToString("g") + "   " ?? ""}{ShortCommitHash}";

    public string TypeLabel => IsAnnotated ? "Annotated" : "Lightweight";

    public string ScopeIndicator => IsRemote ? "R" : "";

    public string CurrentIndicator => IsCurrent ? "*" : ScopeIndicator;

    public GitTag WithCurrentState(bool current)
    {
        return new GitTag(
            Name,
            IsRemote,
            IsAnnotated,
            ObjectHash,
            Subject,
            CreatedDate,
            RemoteName,
            RemoteTagName,
            ReferenceObjectHash,
            current);
    }

    public GitTag WithListMetadataFromMatchingLocalTag(GitTag? localTag)
    {
        if (!IsRemote
            || localTag?.IsLocal != true
            || !RemoteTagName.Equals(localTag.Name, StringComparison.Ordinal)
            || !ReferenceObjectHash.Equals(
                localTag.ReferenceObjectHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return this;
        }

        return new GitTag(
            Name,
            IsRemote,
            IsAnnotated,
            ObjectHash,
            localTag.Subject,
            localTag.CreatedDate,
            RemoteName,
            RemoteTagName,
            ReferenceObjectHash,
            IsCurrent);
    }

    public string Description => $"{CreatedDate?.ToString("g") + "   " ?? ""}" +
                                 $"{ShortCommitHash}" +
                                 $"   {TypeLabel}" +
                                 $"{(string.IsNullOrWhiteSpace(Subject) ? "" : $"   {Subject.Trim()}")}";
}
