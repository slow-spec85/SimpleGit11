using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Services;

public sealed class GitWorktreeService : IGitWorktreeService
{
    private readonly IGitCommandRunner _commandRunner;
    private readonly IExecutionContextService? _executionContextService;

    public GitWorktreeService(
        IGitCommandRunner? commandRunner = null,
        IExecutionContextService? executionContextService = null)
    {
        _commandRunner = commandRunner ?? new GitCommandRunner();
        _executionContextService = executionContextService;
    }

    public async Task<IReadOnlyList<GitWorktree>> GetWorktreesAsync(RepositoryInfo repository)
    {
        string output = await RunGitAsync(repository, "worktree", "list", "--porcelain", "-z");
        return GitWorktreeParser.Parse(
            output,
            repository,
            _executionContextService?.Current.Runtime.Paths);
    }

    public Task AddAsync(RepositoryInfo repository, WorktreeCreationRequest request)
    {
        List<string> arguments = ["worktree", "add"];
        if (request.IsDetached)
        {
            arguments.Add("--detach");
        }
        else if (!string.IsNullOrWhiteSpace(request.NewBranchName))
        {
            arguments.Add("-b");
            arguments.Add(request.NewBranchName);
        }

        if (request.IsLocked)
        {
            arguments.Add("--lock");
        }

        arguments.Add(request.Path);
        if (!string.IsNullOrWhiteSpace(request.StartPoint))
        {
            arguments.Add(request.StartPoint);
        }

        return RunGitAsync(repository, arguments.ToArray());
    }

    public Task MoveAsync(RepositoryInfo repository, GitWorktree worktree, string newPath)
    {
        return RunGitAsync(repository, "worktree", "move", worktree.Path, newPath);
    }

    public Task RemoveAsync(RepositoryInfo repository, GitWorktree worktree, bool force)
    {
        return force
            ? RunGitAsync(repository, "worktree", "remove", "--force", worktree.Path)
            : RunGitAsync(repository, "worktree", "remove", worktree.Path);
    }

    public Task LockAsync(RepositoryInfo repository, GitWorktree worktree, string reason)
    {
        return string.IsNullOrWhiteSpace(reason)
            ? RunGitAsync(repository, "worktree", "lock", worktree.Path)
            : RunGitAsync(repository, "worktree", "lock", "--reason", reason, worktree.Path);
    }

    public Task UnlockAsync(RepositoryInfo repository, GitWorktree worktree)
    {
        return RunGitAsync(repository, "worktree", "unlock", worktree.Path);
    }

    public Task<string> GetPrunePreviewAsync(RepositoryInfo repository)
    {
        return RunGitAsync(repository, "worktree", "prune", "--dry-run", "--verbose");
    }

    public Task PruneAsync(RepositoryInfo repository)
    {
        return RunGitAsync(repository, "worktree", "prune", "--verbose");
    }

    public Task RepairAsync(RepositoryInfo repository, string? path = null)
    {
        return string.IsNullOrWhiteSpace(path)
            ? RunGitAsync(repository, "worktree", "repair")
            : RunGitAsync(repository, "worktree", "repair", path);
    }

    private async Task<string> RunGitAsync(RepositoryInfo repository, params string[] arguments)
    {
        bool mainWorktreeExists = _executionContextService is null
            ? Directory.Exists(repository.MainWorktreePath)
            : await _executionContextService.Current.Runtime.Files.DirectoryExistsAsync(
                repository.MainWorktreePath);
        string workingDirectory = mainWorktreeExists
            ? repository.MainWorktreePath
            : repository.Path;
        GitCommandResult result = await _commandRunner.RunAsync(workingDirectory, arguments);
        return result.StandardOutput;
    }
}
