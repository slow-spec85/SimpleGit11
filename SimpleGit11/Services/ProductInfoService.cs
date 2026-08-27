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
    private const string ReleaseListPath = "repos/slow-spec85/SimpleGit11/releases?per_page=100";
    private const string GitHubApiVersion = "2022-11-28";
    private static readonly Uri GitHubApiBaseUri = new("https://api.github.com/");
    private readonly HttpClient _httpClient;
    private readonly Dictionary<bool, ProductReleaseInfo?> _releaseCache = [];
    private readonly SemaphoreSlim _releaseSemaphore = new(1, 1);

    public ProductInfoService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        CurrentVersion = GetCurrentVersion();
    }

    public string ProductName => ProductNameValue;

    public string CurrentVersion { get; }

    public Uri RepositoryUri { get; } = new(RepositoryUrl);

    public async Task<ProductReleaseInfo?> GetLatestReleaseAsync(
        bool includePrereleases,
        CancellationToken cancellationToken)
    {
        await _releaseSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_releaseCache.TryGetValue(includePrereleases, out ProductReleaseInfo? cachedRelease))
            {
                return cachedRelease;
            }

            ProductReleaseInfo? release = includePrereleases
                ? await GetLatestPublishedReleaseAsync(cancellationToken).ConfigureAwait(false)
                : await GetLatestStableReleaseAsync(cancellationToken).ConfigureAwait(false);
            _releaseCache[includePrereleases] = release;
            return release;
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

    private async Task<ProductReleaseInfo?> GetLatestPublishedReleaseAsync(
        CancellationToken cancellationToken)
    {
        GitHubRelease[]? releases = await GetFromGitHubAsync<GitHubRelease[]>(
            ReleaseListPath,
            cancellationToken).ConfigureAwait(false);
        return releases?
            .Where(static release => !release.Draft)
            .OrderByDescending(static release => release.PublishedAt ?? release.CreatedAt)
            .Select(CreateReleaseInfo)
            .FirstOrDefault(static release => release is not null);
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

        return new ProductReleaseInfo(version, releaseUri, release.Prerelease);
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
    }
}
