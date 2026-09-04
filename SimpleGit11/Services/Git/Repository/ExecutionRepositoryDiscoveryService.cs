using System;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Execution;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services;

public sealed class ExecutionRepositoryDiscoveryService : IExecutionRepositoryDiscoveryService
{
    private readonly IGitCommandRunner _commandRunner;
    private readonly IExecutionContextService _executionContextService;

    public ExecutionRepositoryDiscoveryService(
        IGitCommandRunner commandRunner,
        IExecutionContextService executionContextService)
    {
        _commandRunner = commandRunner;
        _executionContextService = executionContextService;
    }

    public async Task<RepositoryInfo?> TryOpenRepositoryAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        IRepositoryPathService paths = _executionContextService.Current.Runtime.Paths;
        GitCommandResult rootResult = await RunAsync(
            path,
            ["rev-parse", "--show-toplevel"],
            cancellationToken);
        if (!rootResult.IsSuccess || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
        {
            return null;
        }

        string root = paths.Normalize(rootResult.StandardOutput.Trim());
        Task<GitCommandResult> gitDirectoryTask = RunAsync(
            root,
            ["rev-parse", "--path-format=absolute", "--git-dir"],
            cancellationToken);
        Task<GitCommandResult> commonDirectoryTask = RunAsync(
            root,
            ["rev-parse", "--path-format=absolute", "--git-common-dir"],
            cancellationToken);
        Task<GitCommandResult> branchTask = RunAsync(
            root,
            ["symbolic-ref", "--quiet", "--short", "HEAD"],
            cancellationToken);
        await Task.WhenAll(gitDirectoryTask, commonDirectoryTask, branchTask);

        GitCommandResult gitDirectoryResult = await gitDirectoryTask;
        GitCommandResult commonDirectoryResult = await commonDirectoryTask;
        if (!gitDirectoryResult.IsSuccess || !commonDirectoryResult.IsSuccess)
        {
            return null;
        }

        string gitDirectory = paths.Normalize(gitDirectoryResult.StandardOutput.Trim());
        string commonDirectory = paths.Normalize(commonDirectoryResult.StandardOutput.Trim());
        GitCommandResult branchResult = await branchTask;
        string branch = branchResult.IsSuccess
            ? branchResult.StandardOutput.Trim()
            : string.Empty;
        if (string.IsNullOrWhiteSpace(branch))
        {
            GitCommandResult headResult = await RunAsync(
                root,
                ["rev-parse", "--short=7", "HEAD"],
                cancellationToken);
            branch = headResult.IsSuccess
                ? $"Detached at {headResult.StandardOutput.Trim()}"
                : "Detached HEAD";
        }

        bool isMainWorktree = string.Equals(
            gitDirectory,
            commonDirectory,
            StringComparisonFor(paths.Style));
        string mainWorktreePath = isMainWorktree
            ? root
            : paths.GetParent(commonDirectory) ?? root;
        return new RepositoryInfo(
            root,
            paths.GetFileName(root),
            branch,
            commonDirectory,
            mainWorktreePath,
            isMainWorktree);
    }

    private Task<GitCommandResult> RunAsync(
        string workingDirectory,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        return _commandRunner.RunAsync(
            workingDirectory,
            arguments,
            new GitCommandOptions(ThrowOnError: false),
            cancellationToken);
    }

    private static StringComparison StringComparisonFor(RepositoryPathStyle style)
    {
        return style == RepositoryPathStyle.Windows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }
}
