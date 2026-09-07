using System;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IProductUpdateService
{
    bool IsUpdateAvailable(ProductReleaseInfo release, string currentVersion);

    Task<string> DownloadInstallerAsync(
        ProductReleaseInfo release,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}
