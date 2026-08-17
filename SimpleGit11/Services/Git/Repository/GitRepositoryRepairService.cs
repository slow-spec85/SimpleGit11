using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services;

public sealed class GitRepositoryRepairService : IGitRepositoryRepairService
{
    private readonly IGitCommandRunner _commandRunner;

    public GitRepositoryRepairService(IGitCommandRunner? commandRunner = null)
    {
        _commandRunner = commandRunner ?? new GitCommandRunner();
    }

    public bool IsMissingObjectHistoryError(GitCommandException exception)
    {
        return IsMissingObjectHistoryError(exception.Message);
    }

    public async Task<GitRepositoryRepairResult> RepairMissingObjectsAsync(RepositoryInfo repository)
    {
        string remotes = await RunGitAsync(repository, "remote");
        if (string.IsNullOrWhiteSpace(remotes))
        {
            return new GitRepositoryRepairResult(false, "");
        }

        string output;
        try
        {
            output = await RunGitAsync(
                repository,
                "fetch",
                "--all",
                "--prune",
                "--tags",
                "--force",
                "--refetch",
                "--progress");
        }
        catch (GitCommandException exception) when (IsUnsupportedRefetchOption(exception.Message))
        {
            output = await RunGitAsync(
                repository,
                "fetch",
                "--all",
                "--prune",
                "--tags",
                "--force",
                "--progress");
        }

        return new GitRepositoryRepairResult(true, output);
    }

    private static bool IsMissingObjectHistoryError(string message)
    {
        return message.Contains("Could not read", StringComparison.OrdinalIgnoreCase)
            && message.Contains("Failed to traverse parents of commit", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnsupportedRefetchOption(string message)
    {
        return message.Contains("unknown option", StringComparison.OrdinalIgnoreCase)
            && message.Contains("refetch", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> RunGitAsync(RepositoryInfo repository, params string[] arguments)
    {
        GitCommandResult result = await _commandRunner.RunAsync(
            repository.Path,
            arguments,
            new GitCommandOptions(ThrowOnError: false));
        string combinedOutput = result.CombinedOutput;
        if (!result.IsSuccess)
        {
            throw new GitCommandException(
                string.IsNullOrWhiteSpace(combinedOutput) ? "Git repair command failed." : combinedOutput,
                result.ExitCode);
        }

        return combinedOutput;
    }

    private static string CombineOutput(string output, string error)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return error.Trim();
        }

        if (string.IsNullOrWhiteSpace(error))
        {
            return output.Trim();
        }

        return $"{output.Trim()}{Environment.NewLine}{error.Trim()}";
    }
}
