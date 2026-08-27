using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitSubmoduleService
{
    Task<IReadOnlyList<GitSubmodule>> GetSubmodulesAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitSubmoduleReferenceChange>> GetReferenceChangesAsync(
        string repositoryPath,
        string? oldRevision,
        string newRevision,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitSubmoduleApplicationState>> GetApplicationStatesAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        RepositoryInfo repository,
        SubmoduleAddRequest request,
        CancellationToken cancellationToken = default);

    Task InitializeAsync(
        string repositoryPath,
        string? submodulePath = null,
        bool recursive = true,
        CancellationToken cancellationToken = default);

    Task CheckoutRecordedAsync(
        string repositoryPath,
        string? submodulePath = null,
        bool recursive = true,
        CancellationToken cancellationToken = default);

    Task UpdateFromRemoteAsync(
        string repositoryPath,
        string? submodulePath = null,
        bool recursive = true,
        CancellationToken cancellationToken = default);

    Task SyncAsync(
        string repositoryPath,
        string? submodulePath = null,
        bool recursive = true,
        CancellationToken cancellationToken = default);

    Task ApplyPinnedAsync(
        string repositoryPath,
        string? submodulePath = null,
        bool recursive = true,
        CancellationToken cancellationToken = default);

    Task SetUrlAsync(
        string repositoryPath,
        string submodulePath,
        string url,
        CancellationToken cancellationToken = default);

    Task SetBranchAsync(
        string repositoryPath,
        string submodulePath,
        string branch,
        CancellationToken cancellationToken = default);

    Task DeinitializeAsync(
        string repositoryPath,
        string submodulePath,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        string repositoryPath,
        string submodulePath,
        CancellationToken cancellationToken = default);
}
