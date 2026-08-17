using System.Collections.Generic;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitReferenceDetailsService
{
    Task<GitCommit> GetBranchCommitAsync(RepositoryInfo repository, GitBranch branch);

    Task<GitBranchDetails> GetBranchComparisonAsync(RepositoryInfo repository, GitBranch branch);

    Task<IReadOnlyList<GitWorktree>> GetBranchWorktreesAsync(RepositoryInfo repository, GitBranch branch);

    Task<IReadOnlyList<GitReflogEntry>> GetBranchReflogAsync(RepositoryInfo repository, GitBranch branch);

    Task<GitTagDetails> GetTagDetailsAsync(RepositoryInfo repository, GitTag tag);

    Task<GitTagSignatureDetails> GetTagSignatureAsync(RepositoryInfo repository, GitTag tag);

    Task<GitTagRelationDetails> GetTagRelationAsync(RepositoryInfo repository, GitTag tag);

    Task<IReadOnlyList<GitWorktree>> GetTagWorktreesAsync(RepositoryInfo repository, GitTag tag);
}
