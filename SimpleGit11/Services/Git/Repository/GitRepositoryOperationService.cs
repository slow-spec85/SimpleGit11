using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services;

public sealed class GitRepositoryOperationService : IGitRepositoryOperationService
{
    private readonly IGitRepositoryDiscoveryService _repositoryDiscoveryService;
    private readonly IGitCommandRunner _commandRunner;

    public GitRepositoryOperationService(
        IGitRepositoryDiscoveryService repositoryDiscoveryService,
        IGitCommandRunner? commandRunner = null)
    {
        _repositoryDiscoveryService = repositoryDiscoveryService;
        _commandRunner = commandRunner ?? new GitCommandRunner();
    }

    public async Task<RepositoryInfo> CreateAsync(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(path);
        }

        await RunGitAsync(path, "init");
        return _repositoryDiscoveryService.TryOpenRepository(path)
            ?? throw new GitCommandException("Git repository was initialized, but could not be opened.", -1);
    }

    public async Task<RepositoryInfo> CloneAsync(
        string parentPath,
        string remoteUrl,
        bool initializeSubmodulesRecursively = false)
    {
        if (!Directory.Exists(parentPath))
        {
            throw new DirectoryNotFoundException(parentPath);
        }

        string repositoryName = GetRepositoryName(remoteUrl);
        string repositoryPath = Path.Combine(parentPath, repositoryName);
        if (initializeSubmodulesRecursively)
        {
            await RunGitAsync(parentPath, "clone", "--progress", "--recurse-submodules", remoteUrl);
        }
        else
        {
            await RunGitAsync(parentPath, "clone", "--progress", remoteUrl);
        }

        return _repositoryDiscoveryService.TryOpenRepository(repositoryPath)
            ?? throw new GitCommandException("Git repository was cloned, but could not be opened.", -1);
    }

    private async Task RunGitAsync(string workingDirectory, params string[] arguments)
    {
        _ = await _commandRunner.RunAsync(workingDirectory, arguments);
    }

    private static string GetRepositoryName(string remoteUrl)
    {
        string trimmedUrl = remoteUrl.Trim().TrimEnd('/', '\\');
        int separatorIndex = Math.Max(trimmedUrl.LastIndexOf('/'), trimmedUrl.LastIndexOf(':'));
        string name = separatorIndex >= 0 ? trimmedUrl[(separatorIndex + 1)..] : trimmedUrl;
        return name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
    }
}
