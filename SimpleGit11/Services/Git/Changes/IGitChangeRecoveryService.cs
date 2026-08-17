using System.Collections.Generic;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitChangeRecoveryService
{
    Task DiscardFileAsync(RepositoryInfo repository, GitChangedFile changedFile);

    Task DiscardFilesAsync(RepositoryInfo repository, IReadOnlyList<GitChangedFile> changedFiles);

    Task DiscardUnstagedChangesAsync(RepositoryInfo repository);

    Task CleanUntrackedFilesAsync(RepositoryInfo repository);

    Task RevertCommitAsync(RepositoryInfo repository, GitCommit commit);

    Task ContinueOperationAsync(RepositoryInfo repository, GitOperationKind operationKind);

    Task SkipOperationAsync(RepositoryInfo repository, GitOperationKind operationKind);

    Task AbortOperationAsync(RepositoryInfo repository, GitOperationKind operationKind);

    Task ResetAsync(RepositoryInfo repository, GitCommit commit, string mode);
}
