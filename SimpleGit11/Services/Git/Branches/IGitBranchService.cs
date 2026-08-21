using System.Collections.Generic;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitBranchService
{
    Task<IReadOnlyList<GitBranch>> GetLocalBranchesAsync(RepositoryInfo repository);

    Task<IReadOnlyList<GitBranch>> GetRemoteBranchesAsync(RepositoryInfo repository);

    Task CheckoutAsync(RepositoryInfo repository, GitBranch branch);

    Task<string> CheckoutRemoteAsync(RepositoryInfo repository, GitBranch branch);

    Task<string> CreateLocalFromRemoteAsync(RepositoryInfo repository, GitBranch branch);

    Task CreateBranchAsync(RepositoryInfo repository, string branchName, string startPointHash);

    Task CreateAndCheckoutBranchAsync(RepositoryInfo repository, string branchName, string startPointHash);

    Task CreateAndCheckoutOrphanBranchAsync(RepositoryInfo repository, string branchName);

    Task CreateOrphanBranchAsync(
        RepositoryInfo repository,
        string branchName,
        string initialCommitMessage);

    Task CreateOrphanBranchFromCommitAsync(
        RepositoryInfo repository,
        string branchName,
        string startPointHash,
        string initialCommitMessage,
        bool checkout);

    Task RenameBranchAsync(RepositoryInfo repository, GitBranch branch, string newBranchName);

    Task DeleteBranchAsync(RepositoryInfo repository, GitBranch branch);

    Task ForceDeleteBranchAsync(RepositoryInfo repository, GitBranch branch);

    Task MergeAsync(
        RepositoryInfo repository,
        GitBranch branch,
        GitBranchMergeOptions options);

    Task PrepareSnapshotAsync(RepositoryInfo repository, GitBranch sourceBranch);

    Task<GitBranchRebaseResult> RebaseAsync(RepositoryInfo repository, GitBranch branch);

    Task AbortMergeAsync(RepositoryInfo repository);

}
