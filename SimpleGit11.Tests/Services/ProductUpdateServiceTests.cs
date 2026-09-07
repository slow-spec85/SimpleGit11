using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class ProductUpdateServiceTests
{
    [TestMethod]
    [DataRow("1.0.0", "1.0.1", true)]
    [DataRow("1.0.0", "1.0.0", false)]
    [DataRow("2.0.0", "1.9.9", false)]
    [DataRow("1.1.0-preview.2", "1.1.0", true)]
    [DataRow("1.1.1-dev.3", "1.1.0", false)]
    [DataRow("Unknown", "1.1.0", false)]
    public void IsUpdateAvailable_ComparesStableSemanticVersions(
        string currentVersion,
        string releaseVersion,
        bool expected)
    {
        using ProductUpdateService service = CreateService(new QueueHttpMessageHandler());
        ProductReleaseInfo release = CreateRelease(releaseVersion);

        bool actual = service.IsUpdateAvailable(release, currentVersion);

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void IsUpdateAvailable_PrereleaseOrMissingInstaller_ReturnsFalse()
    {
        using ProductUpdateService service = CreateService(new QueueHttpMessageHandler());
        ProductReleaseInfo prerelease = CreateRelease("1.1.0-preview.1") with { IsPrerelease = true };
        ProductReleaseInfo missingInstaller = CreateRelease("1.1.0") with { Installer = null };

        Assert.IsFalse(service.IsUpdateAvailable(prerelease, "1.0.0"));
        Assert.IsFalse(service.IsUpdateAvailable(missingInstaller, "1.0.0"));
    }

    [TestMethod]
    public async Task DownloadInstallerAsync_ValidChecksum_WritesVerifiedInstaller()
    {
        byte[] installerBytes = Encoding.UTF8.GetBytes("fixture MSI payload");
        string checksum = Convert.ToHexString(SHA256.HashData(installerBytes));
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(CreateTextResponse($"{checksum}  SimpleGit11-1.2.3-win-x64.msi"));
        handler.Enqueue(CreateBinaryResponse(installerBytes));
        using TemporaryDirectory directory = new();
        using ProductUpdateService service = CreateService(handler, directory.Path);

        string path = await service.DownloadInstallerAsync(
            CreateRelease("1.2.3", installerBytes.LongLength),
            null,
            CancellationToken.None);

        CollectionAssert.AreEqual(installerBytes, await File.ReadAllBytesAsync(path));
        Assert.AreEqual(2, handler.Requests.Count);
        StringAssert.EndsWith(handler.Requests[0].AbsolutePath, ".msi.sha256");
        StringAssert.EndsWith(handler.Requests[1].AbsolutePath, ".msi");
        Assert.AreEqual(0, Directory.GetFiles(directory.Path, "*.partial", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task DownloadInstallerAsync_ChecksumMismatch_RemovesPartialFile()
    {
        byte[] installerBytes = Encoding.UTF8.GetBytes("fixture MSI payload");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(CreateTextResponse(
            $"{new string('0', 64)}  SimpleGit11-1.2.3-win-x64.msi"));
        handler.Enqueue(CreateBinaryResponse(installerBytes));
        using TemporaryDirectory directory = new();
        using ProductUpdateService service = CreateService(handler, directory.Path);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => service.DownloadInstallerAsync(
            CreateRelease("1.2.3", installerBytes.LongLength),
            null,
            CancellationToken.None));

        Assert.AreEqual(0, Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task DownloadInstallerAsync_UntrustedAsset_IsRejectedBeforeNetworkRequest()
    {
        QueueHttpMessageHandler handler = new();
        using TemporaryDirectory directory = new();
        using ProductUpdateService service = CreateService(handler, directory.Path);
        ProductReleaseInfo release = CreateRelease("1.2.3") with
        {
            Installer = new ProductReleaseAsset(
                "SimpleGit11-1.2.3-win-x64.msi",
                new Uri("https://example.com/SimpleGit11-1.2.3-win-x64.msi"),
                new Uri("https://example.com/SimpleGit11-1.2.3-win-x64.msi.sha256"),
                100)
        };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => service.DownloadInstallerAsync(
            release,
            null,
            CancellationToken.None));

        Assert.AreEqual(0, handler.Requests.Count);
    }

    private static ProductUpdateService CreateService(
        QueueHttpMessageHandler handler,
        string? updateRoot = null)
    {
        return new ProductUpdateService(new HttpClient(handler), updateRoot);
    }

    private static ProductReleaseInfo CreateRelease(string version, long size = 100)
    {
        string fileName = $"SimpleGit11-{version}-win-x64.msi";
        string baseUrl = $"https://github.com/slow-spec85/SimpleGit11/releases/download/v{version}/";
        return new ProductReleaseInfo(
            version,
            new Uri($"https://github.com/slow-spec85/SimpleGit11/releases/tag/v{version}"),
            false,
            new ProductReleaseAsset(
                fileName,
                new Uri(baseUrl + fileName),
                new Uri(baseUrl + fileName + ".sha256"),
                size));
    }

    private static HttpResponseMessage CreateTextResponse(string content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.ASCII)
        };
    }

    private static HttpResponseMessage CreateBinaryResponse(byte[] content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
        };
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public List<Uri> Requests { get; } = [];

        public void Enqueue(HttpResponseMessage response)
        {
            _responses.Enqueue(response);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.IsNotNull(request.RequestUri);
            Requests.Add(request.RequestUri);
            if (_responses.Count == 0)
            {
                throw new AssertFailedException("An unexpected HTTP request was sent.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
