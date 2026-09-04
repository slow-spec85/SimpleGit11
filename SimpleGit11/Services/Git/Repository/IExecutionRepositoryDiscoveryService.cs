using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IExecutionRepositoryDiscoveryService
{
    Task<RepositoryInfo?> TryOpenRepositoryAsync(
        string path,
        CancellationToken cancellationToken = default);
}
