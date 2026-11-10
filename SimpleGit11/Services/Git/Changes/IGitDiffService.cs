using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitDiffService
{
    Task<DiffResult> GetDiffAsync(RepositoryInfo repository, GitChangedFile changedFile, bool compareConflictToHead = false);

    Task<string> GetFullFileTextAsync(RepositoryInfo repository, GitChangedFile changedFile);

    Task<DiffResult> GetCommitDiffAsync(RepositoryInfo repository, GitCommit commit, GitChangedFile changedFile);

    Task<string> GetCommitFileTextAsync(RepositoryInfo repository, GitCommit commit, GitChangedFile changedFile);

    Task<IReadOnlyList<GitFileSearchResult>> SearchFilesAsync(
        RepositoryInfo repository, GitCommit? commit, IReadOnlyList<GitChangedFile> files,
        string query, bool changedLinesOnly, CancellationToken cancellationToken, GitChangedFile? preferredFile = null);

    Task<IReadOnlyList<GitFileSearchResult>> SearchHistoryAsync(
        RepositoryInfo repository, string query, bool changedLinesOnly, CancellationToken cancellationToken,
        string revision = "HEAD");

    Task RevertChangeAsync(RepositoryInfo repository, GitChangedFile changedFile, int lineNumber);
}
