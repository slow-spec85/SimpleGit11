using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Services.Execution;
using SimpleGit11.Services.Execution.Local;

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

    public async Task<WorktreeRemovalState> GetRemovalStateAsync(RepositoryInfo repository, GitWorktree worktree)
    {
        GitWorktree? current = await FindWorktreeAsync(repository, worktree.Path);
        WorktreeRemovalBlocker blocker = GetRemovalBlocker(current);
        if (blocker != WorktreeRemovalBlocker.None)
        {
            return new WorktreeRemovalState(blocker);
        }

        // Do not let Git discover a parent repository when the worktree disappeared.
        string gitFile = Combine(worktree.Path, ".git");
        if (!await DirectoryExistsAsync(worktree.Path) || !await FileExistsAsync(gitFile))
        {
            return new WorktreeRemovalState(WorktreeRemovalBlocker.Unavailable);
        }

        GitCommandResult modulesPath = await _commandRunner.RunAsync(worktree.Path,
            ["rev-parse", "--path-format=absolute", "--git-path", "modules"]);
        bool hasSubmodules = await DirectoryExistsAsync(modulesPath.StandardOutput.TrimEnd('\r', '\n'));
        if (!hasSubmodules)
        {
            GitCommandResult index = await _commandRunner.RunAsync(worktree.Path, ["ls-files", "--stage", "-z"]);
            foreach (string entry in index.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!entry.StartsWith("160000 ", StringComparison.Ordinal))
                {
                    continue;
                }

                int separator = entry.IndexOf('\t');
                if (separator < 0)
                {
                    throw new GitCommandException("Invalid Git index entry.", -1);
                }

                string submoduleGitPath = Combine(Combine(worktree.Path, entry[(separator + 1)..]), ".git");
                if (await FileExistsAsync(submoduleGitPath) || await DirectoryExistsAsync(submoduleGitPath))
                {
                    hasSubmodules = true;
                    break;
                }
            }
        }

        GitCommandResult status = await _commandRunner.RunAsync(worktree.Path,
            ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"]);
        return new WorktreeRemovalState(HasSubmodules: hasSubmodules, HasChanges: status.StandardOutput.Length > 0);
    }

    public async Task RemoveAsync(RepositoryInfo repository, GitWorktree worktree, bool force)
    {
        // Refresh protection flags after confirmation; never override a worktree lock.
        GitWorktree? current = await FindWorktreeAsync(repository, worktree.Path);
        if (GetRemovalBlocker(current) != WorktreeRemovalBlocker.None)
        {
            throw new GitCommandException("The worktree is no longer available for removal.", -1);
        }

        await (force
            ? RunGitAsync(repository, "worktree", "remove", "--force", worktree.Path)
            : RunGitAsync(repository, "worktree", "remove", worktree.Path));
    }

    private async Task<GitWorktree?> FindWorktreeAsync(RepositoryInfo repository, string path)
    {
        IRepositoryPathService paths = _executionContextService?.Current.Runtime.Paths ?? new LocalRepositoryPathService();
        StringComparison comparison = paths.Style == RepositoryPathStyle.Windows
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        char separator = paths.Style == RepositoryPathStyle.Windows ? '\\' : '/';
        string normalizedPath = paths.Normalize(path).TrimEnd(separator);
        return (await GetWorktreesAsync(repository)).FirstOrDefault(candidate =>
            string.Equals(paths.Normalize(candidate.Path).TrimEnd(separator), normalizedPath, comparison));
    }

    private static WorktreeRemovalBlocker GetRemovalBlocker(GitWorktree? worktree) => worktree switch
    {
        null => WorktreeRemovalBlocker.Unavailable,
        { IsMain: true } or { IsBare: true } => WorktreeRemovalBlocker.MainOrBare,
        { IsLocked: true } => WorktreeRemovalBlocker.Locked,
        { IsPrunable: true } => WorktreeRemovalBlocker.Unavailable,
        _ => WorktreeRemovalBlocker.None
    };

    private string Combine(string left, string right) =>
        _executionContextService?.Current.Runtime.Paths.Combine(left, right) ?? Path.Combine(left, right);

    private Task<bool> FileExistsAsync(string path) => _executionContextService is null
        ? Task.FromResult(File.Exists(path))
        : _executionContextService.Current.Runtime.Files.FileExistsAsync(path);

    private Task<bool> DirectoryExistsAsync(string path) => _executionContextService is null
        ? Task.FromResult(Directory.Exists(path))
        : _executionContextService.Current.Runtime.Files.DirectoryExistsAsync(path);

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
