using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public sealed class ProductUpdateService : IProductUpdateService, IDisposable
{
    private const long MaximumInstallerSize = 1024L * 1024 * 1024;
    private const int MaximumChecksumLength = 4096;
    private readonly HttpClient _httpClient;
    private readonly string _updateRoot;

    public ProductUpdateService(HttpClient httpClient, string? updateRoot = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _updateRoot = updateRoot ?? Path.Combine(Path.GetTempPath(), "SimpleGit11", "Updates");
    }

    public bool IsUpdateAvailable(ProductReleaseInfo release, string currentVersion)
    {
        ArgumentNullException.ThrowIfNull(release);
        return !release.IsPrerelease
            && release.Installer is not null
            && StableReleaseVersion.IsNewer(currentVersion, release.Version);
    }

    public async Task<string> DownloadInstallerAsync(
        ProductReleaseInfo release,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ProductReleaseAsset asset = release.Installer
            ?? throw new InvalidOperationException("The release does not contain an installer.");
        if (release.IsPrerelease || !StableReleaseVersion.IsStable(release.Version))
        {
            throw new InvalidOperationException("Only stable releases can be downloaded.");
        }

        ValidateAsset(asset);
        string expectedHash = await DownloadChecksumAsync(asset, cancellationToken)
            .ConfigureAwait(false);
        string releaseDirectory = Path.Combine(_updateRoot, release.Version);
        Directory.CreateDirectory(releaseDirectory);
        string destinationPath = Path.Combine(releaseDirectory, asset.FileName);
        string temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".partial";

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, asset.DownloadUri);
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long contentLength
                && contentLength != asset.Size)
            {
                throw new InvalidDataException("The installer size does not match the release metadata.");
            }

            await using Stream source = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using FileStream destination = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
            long bytesReceived = 0;
            try
            {
                while (true)
                {
                    int bytesRead = await source.ReadAsync(buffer, cancellationToken)
                        .ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    bytesReceived += bytesRead;
                    if (bytesReceived > asset.Size || bytesReceived > MaximumInstallerSize)
                    {
                        throw new InvalidDataException("The installer is larger than expected.");
                    }

                    hash.AppendData(buffer, 0, bytesRead);
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                        .ConfigureAwait(false);
                    progress?.Report((double)bytesReceived / asset.Size);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (bytesReceived != asset.Size)
            {
                throw new InvalidDataException("The installer size does not match the release metadata.");
            }

            string actualHash = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The installer checksum is invalid.");
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Close();
            File.Move(temporaryPath, destinationPath, overwrite: true);
            progress?.Report(1);
            return destinationPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private async Task<string> DownloadChecksumAsync(
        ProductReleaseAsset asset,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, asset.ChecksumUri);
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumChecksumLength)
        {
            throw new InvalidDataException("The checksum file is too large.");
        }

        string content = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        if (content.Length > MaximumChecksumLength)
        {
            throw new InvalidDataException("The checksum file is too large.");
        }

        string[] parts = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || parts[0].Length != 64
            || !IsSha256(parts[0])
            || !string.Equals(parts[1].TrimStart('*'), asset.FileName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The checksum file has an invalid format.");
        }

        return parts[0].ToUpper(CultureInfo.InvariantCulture);
    }

    private static void ValidateAsset(ProductReleaseAsset asset)
    {
        if (asset.Size <= 0 || asset.Size > MaximumInstallerSize
            || !IsTrustedDownloadUri(asset.DownloadUri)
            || !IsTrustedDownloadUri(asset.ChecksumUri)
            || !string.Equals(Path.GetFileName(asset.DownloadUri.AbsolutePath), asset.FileName, StringComparison.Ordinal)
            || !string.Equals(Path.GetFileName(asset.ChecksumUri.AbsolutePath), asset.FileName + ".sha256", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The release contains invalid installer metadata.");
        }
    }

    private static bool IsSha256(string value)
    {
        foreach (char character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTrustedDownloadUri(Uri uri)
    {
        return uri.IsAbsoluteUri
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith(
                "/slow-spec85/SimpleGit11/releases/download/",
                StringComparison.OrdinalIgnoreCase);
    }
}
