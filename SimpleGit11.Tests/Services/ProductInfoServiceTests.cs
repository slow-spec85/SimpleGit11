using System.Net;
using System.Net.Http;
using System.Text;
using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class ProductInfoServiceTests
{
    [TestMethod]
    public void NormalizeVersion_RemovesBuildMetadata()
    {
        string version = ProductInfoService.NormalizeVersion(
            "1.2.3-preview.4+abcdef",
            new Version(9, 9, 9, 9));

        Assert.AreEqual("1.2.3-preview.4", version);
    }

    [TestMethod]
    public void NormalizeVersion_UsesAssemblyVersionAsFallback()
    {
        string version = ProductInfoService.NormalizeVersion(null, new Version(2, 3, 4, 5));

        Assert.AreEqual("2.3.4", version);
    }

    [TestMethod]
    public async Task GetLatestReleaseAsync_StableRelease_ReturnsReleaseAndCachesResponse()
    {
        const string responseJson = """
            {
              "tag_name": "v1.2.3",
              "html_url": "https://github.com/slow-spec85/SimpleGit11/releases/tag/v1.2.3",
              "draft": false,
              "prerelease": false,
              "created_at": "2026-08-01T10:00:00Z",
              "published_at": "2026-08-01T11:00:00Z",
              "assets": [
                {
                  "name": "SimpleGit11-1.2.3-win-x64.msi",
                  "browser_download_url": "https://github.com/slow-spec85/SimpleGit11/releases/download/v1.2.3/SimpleGit11-1.2.3-win-x64.msi",
                  "size": 123456
                },
                {
                  "name": "SimpleGit11-1.2.3-win-x64.msi.sha256",
                  "browser_download_url": "https://github.com/slow-spec85/SimpleGit11/releases/download/v1.2.3/SimpleGit11-1.2.3-win-x64.msi.sha256",
                  "size": 96
                }
              ]
            }
            """;
        RecordingHttpMessageHandler handler = new(_ => CreateJsonResponse(responseJson));
        using HttpClient client = new(handler);
        using ProductInfoService service = new(client);

        ProductReleaseInfo? firstRelease = await service.GetLatestReleaseAsync(CancellationToken.None);
        ProductReleaseInfo? secondRelease = await service.GetLatestReleaseAsync(CancellationToken.None);

        Assert.IsNotNull(firstRelease);
        Assert.AreEqual("1.2.3", firstRelease.Version);
        Assert.AreEqual(
            "https://github.com/slow-spec85/SimpleGit11/releases/tag/v1.2.3",
            firstRelease.Uri.AbsoluteUri);
        Assert.IsFalse(firstRelease.IsPrerelease);
        Assert.IsNotNull(firstRelease.Installer);
        Assert.AreEqual("SimpleGit11-1.2.3-win-x64.msi", firstRelease.Installer.FileName);
        Assert.AreEqual(123456, firstRelease.Installer.Size);
        Assert.AreEqual(firstRelease, secondRelease);
        Assert.AreEqual(1, handler.Requests.Count);
        StringAssert.EndsWith(handler.Requests[0].AbsolutePath, "/releases/latest");
    }

    [TestMethod]
    public async Task GetLatestReleaseAsync_NotFound_ReturnsNull()
    {
        RecordingHttpMessageHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using HttpClient client = new(handler);
        using ProductInfoService service = new(client);

        ProductReleaseInfo? release = await service.GetLatestReleaseAsync(CancellationToken.None);

        Assert.IsNull(release);
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.IsNotNull(request.RequestUri);
            Requests.Add(request.RequestUri);
            return Task.FromResult(responseFactory(request));
        }
    }
}
