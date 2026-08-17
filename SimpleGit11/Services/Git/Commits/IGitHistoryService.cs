using System.Collections.Generic;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitHistoryService
{
    Task<IReadOnlyList<GitCommit>> GetCommitsAsync(RepositoryInfo repository);

    Task<GitCommitPage> GetCommitsPageAsync(
        RepositoryInfo repository,
        int skip,
        int count);

    Task<GitCommit> GetLastCommitAsync(RepositoryInfo repository);

    Task<IReadOnlyList<GitChangedFile>> GetChangedFilesAsync(RepositoryInfo repository, GitCommit commit);

    Task<bool> HasLocalCommits(RepositoryInfo repository);
}
