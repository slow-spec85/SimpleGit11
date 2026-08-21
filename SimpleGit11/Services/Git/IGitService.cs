using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services.Git;

public interface IGitService
{
    IGitArchiveService Archive { get; }

    IGitBranchService Branches { get; }

    IGitChangeRecoveryService ChangeRecovery { get; }

    IGitCommitService Commits { get; }

    IGitCommitWorkflowService CommitWorkflow { get; }

    IGitConfigService Configuration { get; }

    IGitDiffService Diff { get; }

    IGitHistoryService History { get; }

    IGitReferenceDetailsService ReferenceDetails { get; }

    IGitRevisionService Revisions { get; }

    IGitRemoteService Remotes { get; }

    IGitRepositoryDiscoveryService RepositoryDiscovery { get; }

    IGitRepositoryOperationService RepositoryOperations { get; }

    IGitRepositoryRepairService RepositoryRepair { get; }

    IGitRepositorySearchService RepositorySearch { get; }

    IGitStagingService Staging { get; }

    IGitStashService Stashes { get; }

    IGitStatusService Status { get; }

    IGitTagService Tags { get; }

    IGitWorktreeService Worktrees { get; }

    Task ExecuteAsync(Func<Task> operation);

    Task<GitStatusSnapshot> GetStatusAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default);

    Task<GitOperationState> GetOperationStateAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitCommit>> GetHistoryAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default);

    Task<GitCommitPage> GetHistoryPageAsync(
        RepositoryInfo repository,
        int skip,
        int count,
        CancellationToken cancellationToken = default);

    Task<GitCommit> GetLastCommitAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitBranch>> GetLocalBranchesAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitBranch>> GetRemoteBranchesAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitTag>> GetLocalTagsAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitRemote>> GetRemotesAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitWorktree>> GetWorktreesAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default);

    Task<GitCurrentBranchRemoteStatus> GetCurrentBranchRemoteStatusAsync(
        RepositoryInfo repository,
        GitRemote? defaultRemote,
        CancellationToken cancellationToken = default);

    Task<DateTimeOffset?> GetLastFetchTimeAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default);
}
