using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitCommitWorkflowService
{
    Task<bool?> PrepareCreateAsync(RepositoryInfo repository);

    Task<GitCommitOperationResult> CreateAsync(
        RepositoryInfo repository,
        string message,
        bool allowEmpty);

    Task<GitCommitOperationResult> AmendAsync(
        RepositoryInfo repository,
        string? message);

    Task<GitCommitOperationResult> CompleteMergeAsync(
        RepositoryInfo repository,
        string message);
}
