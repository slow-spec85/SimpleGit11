using System;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IProductInfoService
{
    string ProductName { get; }

    string CurrentVersion { get; }

    Uri RepositoryUri { get; }

    Task<ProductReleaseInfo?> GetLatestReleaseAsync(
        bool includePrereleases,
        CancellationToken cancellationToken);
}
