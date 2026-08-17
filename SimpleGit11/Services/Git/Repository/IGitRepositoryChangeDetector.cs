using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitRepositoryChangeDetector
{
    Task EnsureBaselineAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default);

    Task<bool> HasChangedAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default);
}
