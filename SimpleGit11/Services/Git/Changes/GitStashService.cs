using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services;

public sealed class GitStashService : IGitStashService
{
    private const char UnitSeparator = '\x1f';
    private readonly IGitCommandRunner _commandRunner;

    public GitStashService(IGitCommandRunner? commandRunner = null)
    {
        _commandRunner = commandRunner ?? new GitCommandRunner();
    }

    public async Task<IReadOnlyList<GitStash>> GetStashesAsync(RepositoryInfo repository)
    {
        string output = await RunGitAsync(
            repository,
            "stash",
            "list",
            $"--format=%gd{UnitSeparator}%h{UnitSeparator}%cr{UnitSeparator}%gs");

        return ParseStashes(output);
    }

    public Task<string> CreateStashAsync(RepositoryInfo repository)
    {
        string message = $"SimpleGit11 stash {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}";
        return RunGitAsync(repository, "stash", "push", "-u", "-m", message);
    }

    public Task<string> ApplyStashAsync(RepositoryInfo repository, GitStash stash)
    {
        return RunGitAsync(repository, "stash", "apply", stash.Reference);
    }

    public Task<string> PopStashAsync(RepositoryInfo repository, GitStash stash)
    {
        return RunGitAsync(repository, "stash", "pop", stash.Reference);
    }

    public Task<string> DropStashAsync(RepositoryInfo repository, GitStash stash)
    {
        return RunGitAsync(repository, "stash", "drop", stash.Reference);
    }

    public Task<string> ClearStashesAsync(RepositoryInfo repository)
    {
        return RunGitAsync(repository, "stash", "clear");
    }

    private static IReadOnlyList<GitStash> ParseStashes(string output)
    {
        List<GitStash> stashes = [];
        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] fields = line.Split(UnitSeparator);
            if (fields.Length >= 4)
            {
                stashes.Add(new GitStash(fields[0], fields[1], fields[2], fields[3]));
            }
        }

        return stashes;
    }

    private async Task<string> RunGitAsync(RepositoryInfo repository, params string[] arguments)
    {
        GitCommandResult result = await _commandRunner.RunAsync(repository.Path, arguments);
        return string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result.StandardError.Trim()
            : result.StandardOutput.Trim();
    }
}
