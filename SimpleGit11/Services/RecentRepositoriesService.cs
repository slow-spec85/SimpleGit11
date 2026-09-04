using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SimpleGit11.Models;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Services;

public sealed class RecentRepositoriesService : IRecentRepositoriesService
{
    private const string SettingsKey = "RecentRepositories";
    private const int MaxRecentRepositories = 8;
    private readonly ILocalSettingsStore _localSettingsStore;
    private readonly IGitRepositoryDiscoveryService _repositoryDiscoveryService;
    private readonly IExecutionContextService _executionContextService;

    public RecentRepositoriesService(
        ILocalSettingsStore localSettingsStore,
        IGitRepositoryDiscoveryService repositoryDiscoveryService,
        IExecutionContextService executionContextService)
    {
        _localSettingsStore = localSettingsStore;
        _repositoryDiscoveryService = repositoryDiscoveryService;
        _executionContextService = executionContextService;
    }

    public IReadOnlyList<RepositoryInfo> Load()
    {
        string? settingsKey = GetSettingsKey();
        if (settingsKey is null)
        {
            return [];
        }

        string? value = _localSettingsStore.GetString(settingsKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            List<RepositoryInfo> storedRepositories =
                JsonSerializer.Deserialize<List<RepositoryInfo>>(value) ?? [];
            IEnumerable<RepositoryInfo> repositories = _executionContextService.Current.IsLocal
                ? storedRepositories.Select(item =>
                    _repositoryDiscoveryService.TryOpenRepository(item.Path) ?? item)
                : storedRepositories;
            return repositories
                .Select(NormalizeRepository)
                .GroupBy(GetRepositoryIdentity, GetPathComparer())
                .Select(group => group.First())
                .Take(MaxRecentRepositories)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public IReadOnlyList<RepositoryInfo> Add(RepositoryInfo repository)
    {
        repository = NormalizeRepository(repository);
        string? settingsKey = GetSettingsKey();
        if (settingsKey is null)
        {
            return [repository];
        }

        string repositoryIdentity = GetRepositoryIdentity(repository);
        StringComparer comparer = GetPathComparer();
        List<RepositoryInfo> repositories = Load()
            .Where(item => !comparer.Equals(GetRepositoryIdentity(item), repositoryIdentity))
            .Prepend(repository)
            .Take(MaxRecentRepositories)
            .ToList();

        _localSettingsStore.SetString(settingsKey, JsonSerializer.Serialize(repositories));
        return repositories;
    }

    public IReadOnlyList<RepositoryInfo> Remove(RepositoryInfo repository)
    {
        string? settingsKey = GetSettingsKey();
        if (settingsKey is null)
        {
            return [];
        }

        string repositoryIdentity = GetRepositoryIdentity(repository);
        StringComparer comparer = GetPathComparer();
        List<RepositoryInfo> repositories = Load()
            .Where(item => !comparer.Equals(GetRepositoryIdentity(item), repositoryIdentity))
            .ToList();

        _localSettingsStore.SetString(settingsKey, JsonSerializer.Serialize(repositories));
        return repositories;
    }

    private RepositoryInfo NormalizeRepository(RepositoryInfo repository)
    {
        return new RepositoryInfo(
            NormalizePath(repository.Path),
            repository.Name,
            repository.CurrentBranch,
            NormalizePath(repository.CommonGitDirectory),
            NormalizePath(repository.MainWorktreePath),
            repository.IsMainWorktree);
    }

    private string NormalizePath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? path
            : _executionContextService.Current.Runtime.Paths.Normalize(path);
    }

    private string GetRepositoryIdentity(RepositoryInfo repository)
    {
        string path = string.IsNullOrWhiteSpace(repository.CommonGitDirectory)
            ? repository.Path
            : repository.CommonGitDirectory;
        char separator = _executionContextService.Current.Runtime.Paths.Style == RepositoryPathStyle.Windows
            ? '\\'
            : '/';
        return NormalizePath(path).TrimEnd(separator);
    }

    private string? GetSettingsKey()
    {
        ExecutionContext context = _executionContextService.Current;
        if (context.IsLocal)
        {
            return SettingsKey;
        }

        return string.IsNullOrWhiteSpace(context.ConnectionProfileId)
            ? null
            : $"{SettingsKey}:{context.ConnectionProfileId}";
    }

    private StringComparer GetPathComparer()
    {
        return _executionContextService.Current.Runtime.Paths.Style == RepositoryPathStyle.Windows
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }
}
