using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Services;

public sealed class GitRepositoryOperationService : IGitRepositoryOperationService
{
    private readonly IGitRepositoryDiscoveryService _repositoryDiscoveryService;
    private readonly IGitCommandRunner _commandRunner;
    private readonly IExecutionContextService? _executionContextService;
    private readonly IExecutionRepositoryDiscoveryService? _executionRepositoryDiscoveryService;

    public GitRepositoryOperationService(
        IGitRepositoryDiscoveryService repositoryDiscoveryService,
        IGitCommandRunner? commandRunner = null,
        IExecutionContextService? executionContextService = null,
        IExecutionRepositoryDiscoveryService? executionRepositoryDiscoveryService = null)
    {
        _repositoryDiscoveryService = repositoryDiscoveryService;
        _commandRunner = commandRunner ?? new GitCommandRunner();
        _executionContextService = executionContextService;
        _executionRepositoryDiscoveryService = executionRepositoryDiscoveryService;
    }

    public async Task<RepositoryInfo> CreateAsync(string path)
    {
        if (!await DirectoryExistsAsync(path))
        {
            throw new DirectoryNotFoundException(path);
        }

        await RunGitAsync(path, "init");
        return await TryOpenRepositoryAsync(path)
            ?? throw new GitCommandException("Git repository was initialized, but could not be opened.", -1);
    }

    public async Task<RepositoryInfo> CloneAsync(
        string parentPath,
        string remoteUrl,
        bool initializeSubmodulesRecursively = false,
        CancellationToken cancellationToken = default)
    {
        if (!await DirectoryExistsAsync(parentPath, cancellationToken))
        {
            throw new DirectoryNotFoundException(parentPath);
        }

        string repositoryName = GetRepositoryName(remoteUrl);
        string repositoryPath = _executionContextService?.Current.Runtime.Paths.Combine(
            parentPath,
            repositoryName) ?? Path.Combine(parentPath, repositoryName);
        if (initializeSubmodulesRecursively)
        {
            await RunGitAsync(
                parentPath,
                cancellationToken,
                "clone",
                "--progress",
                "--recurse-submodules",
                remoteUrl);
        }
        else
        {
            await RunGitAsync(parentPath, cancellationToken, "clone", "--progress", remoteUrl);
        }

        return await TryOpenRepositoryAsync(repositoryPath, cancellationToken)
            ?? throw new GitCommandException("Git repository was cloned, but could not be opened.", -1);
    }

    private Task RunGitAsync(string workingDirectory, params string[] arguments)
    {
        return RunGitAsync(workingDirectory, CancellationToken.None, arguments);
    }

    private async Task RunGitAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        string? remoteUrl = arguments.Length > 1
            && string.Equals(arguments[0], "clone", StringComparison.Ordinal)
            ? arguments[^1]
            : null;
        _ = await _commandRunner.RunAsync(
            workingDirectory,
            arguments,
            remoteUrl is null
                ? null
                : new GitCommandOptions(
                    HttpAuthentication: new GitHttpAuthentication(remoteUrl)),
            cancellationToken: cancellationToken);
    }

    private Task<bool> DirectoryExistsAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        return _executionContextService is null
            ? Task.FromResult(Directory.Exists(path))
            : _executionContextService.Current.Runtime.Files.DirectoryExistsAsync(path, cancellationToken);
    }

    private Task<RepositoryInfo?> TryOpenRepositoryAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        return _executionRepositoryDiscoveryService is null
            ? Task.FromResult(_repositoryDiscoveryService.TryOpenRepository(path))
            : _executionRepositoryDiscoveryService.TryOpenRepositoryAsync(path, cancellationToken);
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
