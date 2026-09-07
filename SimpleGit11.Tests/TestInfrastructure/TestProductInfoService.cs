using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.Tests.TestInfrastructure;

internal sealed class TestProductInfoService(string currentVersion = "1.0.0")
    : IProductInfoService
{
    public string ProductName => "SimpleGit11";

    public string CurrentVersion { get; } = currentVersion;

    public Uri RepositoryUri { get; } = new("https://github.com/slow-spec85/SimpleGit11");

    public ProductReleaseInfo? LatestRelease { get; set; }

    public Exception? ReleaseException { get; set; }

    public int ReleaseRequests { get; private set; }

    public Task<ProductReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        ReleaseRequests++;
        return ReleaseException is null
            ? Task.FromResult(LatestRelease)
            : Task.FromException<ProductReleaseInfo?>(ReleaseException);
    }
}
