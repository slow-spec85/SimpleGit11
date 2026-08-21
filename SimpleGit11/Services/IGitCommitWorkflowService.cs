using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitCommitWorkflowService
{
    Task<GitCommitOperationResult> CreateAsync(
        RepositoryInfo repository,
        string message);

    Task<GitCommitOperationResult> AmendAsync(
        RepositoryInfo repository,
        string? message);

    Task<GitCommitOperationResult> CompleteMergeAsync(
        RepositoryInfo repository,
        string message);
}
