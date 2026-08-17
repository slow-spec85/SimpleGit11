using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public sealed class RecentRepositoriesService : IRecentRepositoriesService
{
    private const string SettingsKey = "RecentRepositories";
    private const int MaxRecentRepositories = 8;
    private readonly ILocalSettingsStore _localSettingsStore;
    private readonly IGitRepositoryDiscoveryService _repositoryDiscoveryService;

    public RecentRepositoriesService(
        ILocalSettingsStore localSettingsStore,
        IGitRepositoryDiscoveryService repositoryDiscoveryService)
    {
        _localSettingsStore = localSettingsStore;
        _repositoryDiscoveryService = repositoryDiscoveryService;
    }

    public IReadOnlyList<RepositoryInfo> Load()
    {
        string? value = _localSettingsStore.GetString(SettingsKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            List<RepositoryInfo> storedRepositories =
                JsonSerializer.Deserialize<List<RepositoryInfo>>(value) ?? [];
            return storedRepositories
                .Select(item => _repositoryDiscoveryService.TryOpenRepository(item.Path) ?? item)
                .GroupBy(GetRepositoryIdentity, StringComparer.OrdinalIgnoreCase)
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
        string repositoryIdentity = GetRepositoryIdentity(repository);
        List<RepositoryInfo> repositories = Load()
            .Where(item => !string.Equals(
                GetRepositoryIdentity(item),
                repositoryIdentity,
                StringComparison.OrdinalIgnoreCase))
            .Prepend(repository)
            .Take(MaxRecentRepositories)
            .ToList();

        _localSettingsStore.SetString(SettingsKey, JsonSerializer.Serialize(repositories));
        return repositories;
    }

    public IReadOnlyList<RepositoryInfo> Remove(RepositoryInfo repository)
    {
        string repositoryIdentity = GetRepositoryIdentity(repository);
        List<RepositoryInfo> repositories = Load()
            .Where(item => !string.Equals(
                GetRepositoryIdentity(item),
                repositoryIdentity,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        _localSettingsStore.SetString(SettingsKey, JsonSerializer.Serialize(repositories));
        return repositories;
    }

    private static string GetRepositoryIdentity(RepositoryInfo repository)
    {
        return string.IsNullOrWhiteSpace(repository.CommonGitDirectory)
            ? repository.Path
            : repository.CommonGitDirectory;
    }
}
