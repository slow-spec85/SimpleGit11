using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitStatusService
{
    Task<GitStatusSnapshot> GetStatusAsync(RepositoryInfo repository);
    Task<int> GetChangedFileCountAsync(RepositoryInfo repository, CancellationToken cancellationToken);

    Task<GitOperationState> GetOperationStateAsync(RepositoryInfo repository);
}
