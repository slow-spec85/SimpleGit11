using System.Collections.Generic;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitWorktreeService
{
    Task<IReadOnlyList<GitWorktree>> GetWorktreesAsync(RepositoryInfo repository);

    Task AddAsync(RepositoryInfo repository, WorktreeCreationRequest request);

    Task MoveAsync(RepositoryInfo repository, GitWorktree worktree, string newPath);

    Task RemoveAsync(RepositoryInfo repository, GitWorktree worktree, bool force);

    Task LockAsync(RepositoryInfo repository, GitWorktree worktree, string reason);

    Task UnlockAsync(RepositoryInfo repository, GitWorktree worktree);

    Task<string> GetPrunePreviewAsync(RepositoryInfo repository);

    Task PruneAsync(RepositoryInfo repository);

    Task RepairAsync(RepositoryInfo repository, string? path = null);
}
