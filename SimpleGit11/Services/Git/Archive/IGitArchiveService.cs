using SimpleGit11.Models;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Services;

public interface IGitArchiveService
{
    Task CreateAsync(
        RepositoryInfo repository,
        GitArchiveRequest request,
        CancellationToken cancellationToken);
}
