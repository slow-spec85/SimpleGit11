using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public sealed class RepositorySearchService : IGitRepositorySearchService
{
    private const string StartPathKey = "RepositorySearchStartPath";
    private const string FoundRepositoriesKey = "RepositorySearchFoundRepositories";
    private const int MaxFoundRepositories = 500;
    private readonly IGitRepositoryDiscoveryService _repositoryDiscoveryService;
    private readonly ILocalSettingsStore _localSettingsStore;

    public RepositorySearchService(
        IGitRepositoryDiscoveryService repositoryDiscoveryService,
        ILocalSettingsStore localSettingsStore)
    {
        _repositoryDiscoveryService = repositoryDiscoveryService;
        _localSettingsStore = localSettingsStore;
    }

    public string LoadStartPath()
    {
        return _localSettingsStore.GetString(StartPathKey) ?? "";
    }

    public void SaveStartPath(string path)
    {
        _localSettingsStore.SetString(StartPathKey, path);
    }

    public IReadOnlyList<RepositoryInfo> LoadFoundRepositories()
    {
        string? value = _localSettingsStore.GetString(FoundRepositoriesKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            List<RepositoryInfo> repositories = JsonSerializer.Deserialize<List<RepositoryInfo>>(value) ?? [];
            return repositories
                .Select(repository => TryOpenMainRepository(repository.Path) ?? repository)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void SaveFoundRepositories(IReadOnlyList<RepositoryInfo> repositories)
    {
        _localSettingsStore.SetString(FoundRepositoriesKey, JsonSerializer.Serialize(repositories));
    }

    public Task<IReadOnlyList<RepositoryInfo>> SearchAsync(string startPath)
    {
        return Task.Run(() => Search(startPath));
    }

    private IReadOnlyList<RepositoryInfo> Search(string startPath)
    {
        if (!Directory.Exists(startPath))
        {
            throw new DirectoryNotFoundException(startPath);
        }

        var found = new List<RepositoryInfo>();
        var pending = new Stack<string>();
        pending.Push(startPath);

        while (pending.Count > 0 && found.Count < MaxFoundRepositories)
        {
            string directory = pending.Pop();
            if (IsRepositoryDirectory(directory))
            {
                RepositoryInfo? repository = TryOpenMainRepository(directory);
                if (repository is not null &&
                    !found.Any(item => string.Equals(
                        GetRepositoryIdentity(item),
                        GetRepositoryIdentity(repository),
                        StringComparison.OrdinalIgnoreCase)))
                {
                    found.Add(repository);
                }

                continue;
            }

            foreach (string childDirectory in EnumerateDirectories(directory))
            {
                pending.Push(childDirectory);
            }
        }

        return found
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Path)
            .ToList();
    }

    private RepositoryInfo? TryOpenMainRepository(string path)
    {
        RepositoryInfo? repository = _repositoryDiscoveryService.TryOpenRepository(path);
        if (repository is null
            || repository.IsMainWorktree
            || string.IsNullOrWhiteSpace(repository.MainWorktreePath))
        {
            return repository;
        }

        return _repositoryDiscoveryService.TryOpenRepository(repository.MainWorktreePath) ?? repository;
    }

    private static bool IsRepositoryDirectory(string directory)
    {
        string gitPath = Path.Combine(directory, ".git");
        return Directory.Exists(gitPath) || File.Exists(gitPath);
    }

    private static string GetRepositoryIdentity(RepositoryInfo repository)
    {
        return string.IsNullOrWhiteSpace(repository.CommonGitDirectory)
            ? repository.Path
            : repository.CommonGitDirectory;
    }

    private static IEnumerable<string> EnumerateDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory);
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }
}
