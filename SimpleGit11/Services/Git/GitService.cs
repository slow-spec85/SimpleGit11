using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services.Git;

internal sealed class GitService : IGitService
{
    private readonly IGitOperationQueue _operationQueue;

    public GitService(
        IGitArchiveService archiveService,
        IGitBranchService branchService,
        IGitChangeRecoveryService changeRecoveryService,
        IGitCommitService commitService,
        IGitCommitWorkflowService commitWorkflowService,
        IGitConfigService configService,
        IGitDiffService diffService,
        IGitHistoryService historyService,
        IGitOperationQueue operationQueue,
        IGitReferenceDetailsService referenceDetailsService,
        IGitRevisionService revisionService,
        IGitRemoteService remoteService,
        IGitRepositoryDiscoveryService repositoryDiscoveryService,
        IGitRepositoryOperationService repositoryOperationService,
        IGitRepositoryRepairService repositoryRepairService,
        IGitRepositorySearchService repositorySearchService,
        IGitStagingService stagingService,
        IGitStashService stashService,
        IGitStatusService statusService,
        IGitSubmoduleService submoduleService,
        IGitTagService tagService,
        IGitWorktreeService worktreeService)
    {
        Archive = archiveService;
        Branches = branchService;
        ChangeRecovery = changeRecoveryService;
        Commits = commitService;
        CommitWorkflow = commitWorkflowService;
        Configuration = configService;
        Diff = diffService;
        History = historyService;
        _operationQueue = operationQueue;
        ReferenceDetails = referenceDetailsService;
        Revisions = revisionService;
        Remotes = remoteService;
        RepositoryDiscovery = repositoryDiscoveryService;
        RepositoryOperations = repositoryOperationService;
        RepositoryRepair = repositoryRepairService;
        RepositorySearch = repositorySearchService;
        Staging = stagingService;
        Stashes = stashService;
        Status = statusService;
        Submodules = submoduleService;
        Tags = tagService;
        Worktrees = worktreeService;
    }

    public IGitArchiveService Archive { get; }

    public IGitBranchService Branches { get; }

    public IGitChangeRecoveryService ChangeRecovery { get; }

    public IGitCommitService Commits { get; }

    public IGitCommitWorkflowService CommitWorkflow { get; }

    public IGitConfigService Configuration { get; }

    public IGitDiffService Diff { get; }

    public IGitHistoryService History { get; }

    public IGitReferenceDetailsService ReferenceDetails { get; }

    public IGitRevisionService Revisions { get; }

    public IGitRemoteService Remotes { get; }

    public IGitRepositoryDiscoveryService RepositoryDiscovery { get; }

    public IGitRepositoryOperationService RepositoryOperations { get; }

    public IGitRepositoryRepairService RepositoryRepair { get; }

    public IGitRepositorySearchService RepositorySearch { get; }

    public IGitStagingService Staging { get; }

    public IGitStashService Stashes { get; }

    public IGitStatusService Status { get; }

    public IGitSubmoduleService Submodules { get; }

    public IGitTagService Tags { get; }

    public IGitWorktreeService Worktrees { get; }

    public Task ExecuteAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return _operationQueue.EnqueueAsync(operation);
    }

    public Task<GitStatusSnapshot> GetStatusAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        cancellationToken.ThrowIfCancellationRequested();
        return Status.GetStatusAsync(repository);
    }

    public Task<GitOperationState> GetOperationStateAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        cancellationToken.ThrowIfCancellationRequested();
        return Status.GetOperationStateAsync(repository);
    }

    public Task<IReadOnlyList<GitCommit>> GetHistoryAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        cancellationToken.ThrowIfCancellationRequested();
        return History.GetCommitsAsync(repository);
    }

    public Task<GitCommitPage> GetHistoryPageAsync(
        RepositoryInfo repository,
        int skip,
        int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        cancellationToken.ThrowIfCancellationRequested();
        return History.GetCommitsPageAsync(repository, skip, count);
    }

    public Task<GitCommit> GetLastCommitAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        cancellationToken.ThrowIfCancellationRequested();
        return History.GetLastCommitAsync(repository);
    }

    public Task<IReadOnlyList<GitBranch>> GetLocalBranchesAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        cancellationToken.ThrowIfCancellationRequested();
        return Branches.GetLocalBranchesAsync(repository);
    }

    public Task<IReadOnlyList<GitBranch>> GetRemoteBranchesAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        cancellationToken.ThrowIfCancellationRequested();
        return Branches.GetRemoteBranchesAsync(repository);
    }

    public Task<IReadOnlyList<GitTag>> GetLocalTagsAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        cancellationToken.ThrowIfCancellationRequested();
        return Tags.GetLocalTagsAsync(repository);
    }

    public Task<IReadOnlyList<GitRemote>> GetRemotesAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return Remotes.GetRemotesAsync(repository, cancellationToken);
    }

    public Task<IReadOnlyList<GitWorktree>> GetWorktreesAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        cancellationToken.ThrowIfCancellationRequested();
        return Worktrees.GetWorktreesAsync(repository);
    }

    public Task<GitCurrentBranchRemoteStatus> GetCurrentBranchRemoteStatusAsync(
        RepositoryInfo repository,
        GitRemote? defaultRemote,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return Remotes.GetCurrentBranchRemoteStatusAsync(
            repository,
            defaultRemote,
            cancellationToken);
    }

    public Task<DateTimeOffset?> GetLastFetchTimeAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return Remotes.GetLastFetchTimeAsync(repository, cancellationToken);
    }
}
