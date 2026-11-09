using System;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IProductUpdateService
{
    bool IsUpdateAvailable(ProductReleaseInfo release, string currentVersion);

    // The callback runs synchronously on the thread executing each stage.
    Task<string> DownloadInstallerAsync(
        ProductReleaseInfo release,
        Action<ProductUpdateStage>? reportStage,
        CancellationToken cancellationToken);
}
