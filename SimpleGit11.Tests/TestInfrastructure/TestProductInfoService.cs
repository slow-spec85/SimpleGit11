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

    public List<bool> IncludePrereleaseRequests { get; } = [];

    public Task<ProductReleaseInfo?> GetLatestReleaseAsync(
        bool includePrereleases,
        CancellationToken cancellationToken)
    {
        IncludePrereleaseRequests.Add(includePrereleases);
        return ReleaseException is null
            ? Task.FromResult(LatestRelease)
            : Task.FromException<ProductReleaseInfo?>(ReleaseException);
    }
}
