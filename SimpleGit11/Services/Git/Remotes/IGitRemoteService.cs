using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitRemoteService
{
    Task<IReadOnlyList<GitRemote>> GetRemotesAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default);

    Task<GitCurrentBranchRemoteStatus> GetCurrentBranchRemoteStatusAsync(
        RepositoryInfo repository,
        GitRemote? defaultRemote,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitCommit>> GetCommitsAsync(RepositoryInfo repository, string revisionRange);

    Task<IReadOnlyList<GitCommit>> GetComparisonCommitsAsync(
        RepositoryInfo repository,
        string leftRevision,
        string rightRevision,
        string leftLabel,
        string rightLabel);

    Task<IReadOnlyList<GitCommit>> GetOutgoingCommitsAsync(
        RepositoryInfo repository,
        GitRemote remote,
        BranchSynchronizationItem branch);

    Task<IReadOnlyList<GitCommit>> GetIncomingCommitsAsync(
        RepositoryInfo repository,
        BranchSynchronizationItem branch);

    Task<SynchronizationSnapshot> GetSynchronizationSnapshotAsync(
        RepositoryInfo repository,
        GitRemote remote,
        CancellationToken cancellationToken = default);

    Task<SynchronizationSnapshot> GetLocalSynchronizationSnapshotAsync(
        RepositoryInfo repository,
        GitRemote remote,
        IReadOnlyList<TagSynchronizationItem> knownTags,
        CancellationToken cancellationToken = default);

    Task<SynchronizationSnapshot> GetConfiguredSynchronizationSnapshotAsync(
        RepositoryInfo repository,
        GitRemote defaultRemote,
        CancellationToken cancellationToken = default);

    Task<SynchronizationSnapshot> GetLocalConfiguredSynchronizationSnapshotAsync(
        RepositoryInfo repository,
        GitRemote defaultRemote,
        IReadOnlyList<TagSynchronizationItem> knownTags,
        CancellationToken cancellationToken = default);

    Task<DateTimeOffset?> GetLastFetchTimeAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> FetchAsync(
        RepositoryInfo repository,
        GitRemote remote,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> FetchBranchesAsync(
        RepositoryInfo repository,
        GitRemote remote,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> FetchSynchronizationRemotesAsync(
        RepositoryInfo repository,
        GitRemote defaultRemote,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> PullAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> PullAsync(
        RepositoryInfo repository,
        string remoteName,
        string branchName,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> PushBranchAsync(
        RepositoryInfo repository,
        string remoteName,
        string branchName,
        bool forceWithLease,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> PushTagAsync(
        RepositoryInfo repository,
        GitRemote remote,
        string tagName,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> PushAsync(
        RepositoryInfo repository,
        GitPushRequest request,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> AddRemoteAsync(
        RepositoryInfo repository,
        string name,
        string url,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> SetRemoteUrlAsync(RepositoryInfo repository, GitRemote remote, string url);

    Task<GitRemoteOperationResult> RenameRemoteAsync(
        RepositoryInfo repository,
        GitRemote remote,
        string newName);


    Task<GitRemoteOperationResult> RemoveRemoteAsync(
        RepositoryInfo repository,
        GitRemote remote,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> DeleteBranchAsync(
        RepositoryInfo repository,
        GitRemote remote,
        string branchName,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> FetchTagAsync(
        RepositoryInfo repository,
        GitRemote remote,
        string tagName,
        bool force,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitTag>> GetRemoteTagsAsync(
        RepositoryInfo repository,
        GitRemote remote,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> DeleteTagAsync(
        RepositoryInfo repository,
        GitRemote remote,
        string tagName,
        CancellationToken cancellationToken = default);
}
