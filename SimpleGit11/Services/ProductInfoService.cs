using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public sealed class ProductInfoService : IProductInfoService, IDisposable
{
    private const string ProductNameValue = "SimpleGit11";
    private const string RepositoryUrl = "https://github.com/slow-spec85/SimpleGit11";
    private const string LatestStableReleasePath = "repos/slow-spec85/SimpleGit11/releases/latest";
    private const string GitHubApiVersion = "2022-11-28";
    private static readonly Uri GitHubApiBaseUri = new("https://api.github.com/");
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _releaseSemaphore = new(1, 1);
    private ProductReleaseInfo? _cachedRelease;
    private bool _releaseLoaded;

    public ProductInfoService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        CurrentVersion = GetCurrentVersion();
    }

    public string ProductName => ProductNameValue;

    public string CurrentVersion { get; }

    public Uri RepositoryUri { get; } = new(RepositoryUrl);

    public async Task<ProductReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        await _releaseSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_releaseLoaded)
            {
                return _cachedRelease;
            }

            _cachedRelease = await GetLatestStableReleaseAsync(cancellationToken).ConfigureAwait(false);
            _releaseLoaded = true;
            return _cachedRelease;
        }
        finally
        {
            _releaseSemaphore.Release();
        }
    }

    public void Dispose()
    {
        _releaseSemaphore.Dispose();
    }

    internal static string NormalizeVersion(string? informationalVersion, Version? fallbackVersion)
    {
        string? normalizedVersion = informationalVersion?.Split('+', 2)[0].Trim();
        if (!string.IsNullOrWhiteSpace(normalizedVersion))
        {
            return normalizedVersion;
        }

        return fallbackVersion is null
            ? "Unknown"
            : $"{fallbackVersion.Major}.{fallbackVersion.Minor}.{fallbackVersion.Build}";
    }

    private static string GetCurrentVersion()
    {
        Assembly assembly = typeof(ProductInfoService).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return NormalizeVersion(informationalVersion, assembly.GetName().Version);
    }

    private async Task<ProductReleaseInfo?> GetLatestStableReleaseAsync(
        CancellationToken cancellationToken)
    {
        GitHubRelease? release = await GetFromGitHubAsync<GitHubRelease>(
            LatestStableReleasePath,
            cancellationToken).ConfigureAwait(false);
        return release is null || release.Draft || release.Prerelease
            ? null
            : CreateReleaseInfo(release);
    }

    private async Task<T?> GetFromGitHubAsync<T>(
        string relativePath,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(GitHubApiBaseUri, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd($"{ProductNameValue}/{CurrentVersion}");
        request.Headers.Add("X-GitHub-Api-Version", GitHubApiVersion);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        response.EnsureSuccessStatusCode();
        await using System.IO.Stream responseStream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(
            responseStream,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static ProductReleaseInfo? CreateReleaseInfo(GitHubRelease release)
    {
        string version = release.TagName.Trim();
        if (version.StartsWith('v'))
        {
            version = version[1..];
        }

        if (string.IsNullOrWhiteSpace(version)
            || !Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out Uri? releaseUri)
            || !string.Equals(releaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(releaseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new ProductReleaseInfo(
            version,
            releaseUri,
            release.Prerelease,
            release.Prerelease ? null : CreateInstallerAsset(release, version));
    }

    private static ProductReleaseAsset? CreateInstallerAsset(
        GitHubRelease release,
        string version)
    {
        string installerName = $"SimpleGit11-{version}-win-x64.msi";
        GitHubReleaseAsset? installer = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, installerName, StringComparison.Ordinal));
        GitHubReleaseAsset? checksum = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, installerName + ".sha256", StringComparison.Ordinal));
        if (installer is null
            || checksum is null
            || installer.Size <= 0
            || !TryCreateDownloadUri(installer.DownloadUrl, out Uri installerUri)
            || !TryCreateDownloadUri(checksum.DownloadUrl, out Uri checksumUri))
        {
            return null;
        }

        return new ProductReleaseAsset(installerName, installerUri, checksumUri, installer.Size);
    }

    private static bool TryCreateDownloadUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? candidate)
            && string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            && candidate.AbsolutePath.StartsWith(
                "/slow-spec85/SimpleGit11/releases/download/",
                StringComparison.OrdinalIgnoreCase))
        {
            uri = candidate;
            return true;
        }

        uri = null!;
        return false;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = "";

        [JsonPropertyName("draft")]
        public bool Draft { get; init; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; init; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; init; }

        [JsonPropertyName("assets")]
        public GitHubReleaseAsset[] Assets { get; init; } = [];
    }

    private sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; init; } = "";

        [JsonPropertyName("size")]
        public long Size { get; init; }
    }
}
