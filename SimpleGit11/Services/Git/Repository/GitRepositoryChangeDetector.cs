using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services;

public sealed class GitRepositoryChangeDetector : IGitRepositoryChangeDetector
{
    private static readonly IReadOnlyList<string> StatusArguments =
    [
        "status",
        "--porcelain=v2",
        "-z",
        "--branch",
        "--no-ahead-behind",
        "--untracked-files=normal",
        "--no-renames"
    ];

    private static readonly IReadOnlyList<string> ReferenceArguments =
    [
        "for-each-ref",
        "--sort=refname",
        "--format=%(refname)%00%(objectname)"
    ];

    private readonly IGitCommandRunner _commandRunner;
    private readonly IGitStatusService _statusService;
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, string> _snapshots =
        new(StringComparer.OrdinalIgnoreCase);

    public GitRepositoryChangeDetector(
        IGitCommandRunner commandRunner,
        IGitStatusService statusService)
    {
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
        _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
    }

    public async Task EnsureBaselineAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        string repositoryPath = NormalizePath(repository.Path);
        lock (_syncRoot)
        {
            if (_snapshots.ContainsKey(repositoryPath))
            {
                return;
            }
        }

        string snapshot = await CreateSnapshotAsync(repository, cancellationToken);
        lock (_syncRoot)
        {
            if (!_snapshots.ContainsKey(repositoryPath))
            {
                _snapshots[repositoryPath] = snapshot;
            }
        }
    }

    public async Task<bool> HasChangedAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        string snapshot = await CreateSnapshotAsync(repository, cancellationToken);
        string repositoryPath = NormalizePath(repository.Path);

        lock (_syncRoot)
        {
            bool hasPreviousSnapshot = _snapshots.TryGetValue(
                repositoryPath,
                out string? previousSnapshot);
            _snapshots[repositoryPath] = snapshot;
            return !hasPreviousSnapshot
                || !string.Equals(previousSnapshot, snapshot, StringComparison.Ordinal);
        }
    }

    private async Task<string> CreateSnapshotAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        Task<GitCommandResult> statusTask = _commandRunner.RunAsync(
            repository.Path,
            StatusArguments,
            cancellationToken: cancellationToken);
        Task<GitCommandResult> referencesTask = _commandRunner.RunAsync(
            repository.Path,
            ReferenceArguments,
            cancellationToken: cancellationToken);
        Task<GitOperationState> operationStateTask =
            _statusService.GetOperationStateAsync(repository);

        await Task.WhenAll(statusTask, referencesTask, operationStateTask);

        string statusOutput = (await statusTask).StandardOutput;
        string referencesOutput = (await referencesTask).StandardOutput;
        GitOperationState operationState = await operationStateTask;
        return string.Concat(
            statusOutput.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            statusOutput,
            referencesOutput,
            operationState.Kind,
            operationState.PreparedCommitMessage);
    }

    private static string NormalizePath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
