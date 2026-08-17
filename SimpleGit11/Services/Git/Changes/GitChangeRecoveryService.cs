using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services;

public sealed class GitChangeRecoveryService : IGitChangeRecoveryService
{
    private readonly IGitCommandRunner _commandRunner;

    public GitChangeRecoveryService(IGitCommandRunner? commandRunner = null)
    {
        _commandRunner = commandRunner ?? new GitCommandRunner();
    }

    public async Task DiscardFileAsync(RepositoryInfo repository, GitChangedFile changedFile)
    {
        IReadOnlyList<IReadOnlyList<string>> commands =
            GitChangeRecoveryArguments.CreateDiscardFileCommands(changedFile);
        foreach (IReadOnlyList<string> arguments in commands)
        {
            await RunGitAsync(repository, arguments.ToArray());
        }
    }

    public async Task DiscardFilesAsync(
        RepositoryInfo repository,
        IReadOnlyList<GitChangedFile> changedFiles)
    {
        ArgumentNullException.ThrowIfNull(changedFiles);

        IEnumerable<GitChangedFile> changesByPath = changedFiles
            .GroupBy(changedFile => changedFile.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());

        foreach (GitChangedFile changedFile in changesByPath)
        {
            await DiscardFileAsync(repository, changedFile);
        }
    }

    public async Task DiscardUnstagedChangesAsync(RepositoryInfo repository)
    {
        await RunGitAsync(repository, "restore", "--worktree", "--", ".");
        await RunGitAsync(repository, "clean", "-fd");
    }

    public Task CleanUntrackedFilesAsync(RepositoryInfo repository)
    {
        return RunGitAsync(repository, "clean", "-fd");
    }

    public Task RevertCommitAsync(RepositoryInfo repository, GitCommit commit)
    {
        return RunGitAsync(repository, "revert", "--no-edit", commit.Hash);
    }

    public Task ContinueOperationAsync(
        RepositoryInfo repository,
        GitOperationKind operationKind)
    {
        return RunOperationAsync(
            repository,
            operationKind,
            "--continue",
            disableEditor: true);
    }

    public Task SkipOperationAsync(
        RepositoryInfo repository,
        GitOperationKind operationKind)
    {
        return RunOperationAsync(repository, operationKind, "--skip");
    }

    public Task AbortOperationAsync(
        RepositoryInfo repository,
        GitOperationKind operationKind)
    {
        return RunOperationAsync(repository, operationKind, "--abort");
    }

    public Task ResetAsync(RepositoryInfo repository, GitCommit commit, string mode)
    {
        IReadOnlyList<string> arguments =
            GitChangeRecoveryArguments.CreateResetArguments(commit.Hash, mode);
        return RunGitAsync(repository, arguments.ToArray());
    }

    private async Task RunGitAsync(RepositoryInfo repository, params string[] arguments)
    {
        _ = await _commandRunner.RunAsync(repository.Path, arguments);
    }

    private async Task RunOperationAsync(
        RepositoryInfo repository,
        GitOperationKind operationKind,
        string action,
        bool disableEditor = false)
    {
        string command = operationKind switch
        {
            GitOperationKind.Rebase => "rebase",
            GitOperationKind.CherryPick => "cherry-pick",
            GitOperationKind.Revert => "revert",
            _ => throw new ArgumentOutOfRangeException(
                nameof(operationKind),
                operationKind,
                "The Git operation does not support sequencer actions.")
        };
        IReadOnlyDictionary<string, string>? environmentVariables = disableEditor
            ? new Dictionary<string, string> { ["GIT_EDITOR"] = "true" }
            : null;

        _ = await _commandRunner.RunAsync(
            repository.Path,
            [command, action],
            new GitCommandOptions(EnvironmentVariables: environmentVariables));
    }
}
