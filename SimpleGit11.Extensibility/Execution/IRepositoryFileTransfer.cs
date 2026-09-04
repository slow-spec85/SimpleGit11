using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Services.Execution;

public interface IRepositoryFileTransfer
{
    Task DownloadAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
