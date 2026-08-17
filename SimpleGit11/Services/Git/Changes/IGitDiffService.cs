using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitDiffService
{
    Task<DiffResult> GetDiffAsync(RepositoryInfo repository, GitChangedFile changedFile);

    Task<string> GetFullFileTextAsync(RepositoryInfo repository, GitChangedFile changedFile);

    Task<DiffResult> GetCommitDiffAsync(RepositoryInfo repository, GitCommit commit, GitChangedFile changedFile);

    Task<string> GetCommitFileTextAsync(RepositoryInfo repository, GitCommit commit, GitChangedFile changedFile);

    Task RevertChangeAsync(RepositoryInfo repository, GitChangedFile changedFile, int lineNumber);
}
