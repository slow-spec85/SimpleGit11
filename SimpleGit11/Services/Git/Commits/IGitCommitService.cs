using System.Collections.Generic;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitCommitService
{
    Task<string> CommitAsync(
        RepositoryInfo repository,
        string message,
        GitCommitOptions options);

    Task<string> AmendAsync(
        RepositoryInfo repository,
        string? message,
        GitCommitOptions options);

    Task<bool> WouldCreateEmptyCommitAsync(RepositoryInfo repository, bool amend);

    Task CherryPickAsync(
        RepositoryInfo repository,
        IReadOnlyList<GitCommit> commits,
        GitCherryPickOptions options);
}
