using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Services;

public sealed class RepositorySearchService : IGitRepositorySearchService
{
    private const string StartPathKey = "RepositorySearchStartPath";
    private const string FoundRepositoriesKey = "RepositorySearchFoundRepositories";
    private const int MaxFoundRepositories = 500;
    private readonly IGitRepositoryDiscoveryService _localRepositoryDiscoveryService;
    private readonly IExecutionRepositoryDiscoveryService _executionRepositoryDiscoveryService;
    private readonly ILocalSettingsStore _localSettingsStore;
    private readonly IExecutionContextService _executionContextService;

    public RepositorySearchService(
        IGitRepositoryDiscoveryService localRepositoryDiscoveryService,
        IExecutionRepositoryDiscoveryService executionRepositoryDiscoveryService,
        ILocalSettingsStore localSettingsStore,
        IExecutionContextService executionContextService)
    {
        _localRepositoryDiscoveryService = localRepositoryDiscoveryService;
        _executionRepositoryDiscoveryService = executionRepositoryDiscoveryService;
        _localSettingsStore = localSettingsStore;
        _executionContextService = executionContextService;
    }

    public string LoadStartPath()
    {
        string? settingsKey = GetSettingsKey(StartPathKey);
        return settingsKey is null ? "" : _localSettingsStore.GetString(settingsKey) ?? "";
    }

    public void SaveStartPath(string path)
    {
        string? settingsKey = GetSettingsKey(StartPathKey);
        if (settingsKey is not null)
        {
            _localSettingsStore.SetString(settingsKey, path);
        }
    }

    public IReadOnlyList<RepositoryInfo> LoadFoundRepositories()
    {
        string? settingsKey = GetSettingsKey(FoundRepositoriesKey);
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
            return JsonSerializer.Deserialize<List<RepositoryInfo>>(value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void SaveFoundRepositories(IReadOnlyList<RepositoryInfo> repositories)
    {
        string? settingsKey = GetSettingsKey(FoundRepositoriesKey);
        if (settingsKey is not null)
        {
            _localSettingsStore.SetString(settingsKey, JsonSerializer.Serialize(repositories));
        }
    }

    public async Task<IReadOnlyList<RepositoryInfo>> SearchAsync(string startPath)
    {
        IExecutionRuntime runtime = _executionContextService.Current.Runtime;
        if (!await runtime.Files.DirectoryExistsAsync(startPath))
        {
            throw new DirectoryNotFoundException(startPath);
        }

        List<RepositoryInfo> found = [];
        Stack<string> pending = [];
        StringComparer pathComparer = runtime.Paths.Style == RepositoryPathStyle.Windows
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        HashSet<string> visited = new(pathComparer);
        pending.Push(startPath);

        while (pending.Count > 0 && found.Count < MaxFoundRepositories)
        {
            string directory = pending.Pop();
            if (!visited.Add(runtime.Paths.Normalize(directory)))
            {
                continue;
            }

            if (await IsRepositoryDirectoryAsync(directory, runtime))
            {
                RepositoryInfo? repository = await TryOpenMainRepositoryAsync(directory);
                if (repository is not null &&
                    !found.Any(item => pathComparer.Equals(
                        GetRepositoryIdentity(item),
                        GetRepositoryIdentity(repository))))
                {
                    found.Add(repository);
                }

                continue;
            }

            foreach (string childDirectory in await runtime.Files.EnumerateDirectoriesAsync(directory))
            {
                pending.Push(childDirectory);
            }
        }

        return found
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Path)
            .ToList();
    }

    private async Task<RepositoryInfo?> TryOpenMainRepositoryAsync(string path)
    {
        RepositoryInfo? repository = await TryOpenRepositoryAsync(path);
        if (repository is null
            || repository.IsMainWorktree
            || string.IsNullOrWhiteSpace(repository.MainWorktreePath))
        {
            return repository;
        }

        return await TryOpenRepositoryAsync(repository.MainWorktreePath)
            ?? repository;
    }

    private Task<RepositoryInfo?> TryOpenRepositoryAsync(string path)
    {
        return _executionContextService.Current.IsLocal
            ? Task.FromResult(_localRepositoryDiscoveryService.TryOpenRepository(path))
            : _executionRepositoryDiscoveryService.TryOpenRepositoryAsync(path);
    }

    private static async Task<bool> IsRepositoryDirectoryAsync(
        string directory,
        IExecutionRuntime runtime)
    {
        string gitPath = runtime.Paths.Combine(directory, ".git");
        Task<bool> directoryTask = runtime.Files.DirectoryExistsAsync(gitPath);
        Task<bool> fileTask = runtime.Files.FileExistsAsync(gitPath);
        await Task.WhenAll(directoryTask, fileTask);
        return await directoryTask || await fileTask;
    }

    private static string GetRepositoryIdentity(RepositoryInfo repository)
    {
        return string.IsNullOrWhiteSpace(repository.CommonGitDirectory)
            ? repository.Path
            : repository.CommonGitDirectory;
    }

    private string? GetSettingsKey(string baseKey)
    {
        ExecutionContext context = _executionContextService.Current;
        if (context.IsLocal)
        {
            return baseKey;
        }

        return string.IsNullOrWhiteSpace(context.ConnectionProfileId)
            ? null
            : $"{baseKey}:{context.ConnectionProfileId}";
    }
}
