using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services;

public sealed class GitCommitService : IGitCommitService
{
    private readonly IGitCommandRunner _commandRunner;

    public GitCommitService(IGitCommandRunner? commandRunner = null)
    {
        _commandRunner = commandRunner ?? new GitCommandRunner();
    }

    public Task<string> CommitAsync(
        RepositoryInfo repository,
        string message,
        GitCommitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        List<string> arguments = ["commit"];
        AddAllowEmptyArgument(arguments, options);
        AddMessageArguments(arguments, message);
        return RunGitAsync(repository, [.. arguments]);
    }

    public Task<string> AmendAsync(
        RepositoryInfo repository,
        string? message,
        GitCommitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        List<string> arguments = ["commit", "--amend"];
        AddAllowEmptyArgument(arguments, options);
        if (!string.IsNullOrWhiteSpace(message))
        {
            AddMessageArguments(arguments, message);
        }
        else
        {
            arguments.Add("--no-edit");
        }

        return RunGitAsync(repository, [.. arguments]);
    }

    public async Task<bool> WouldCreateEmptyCommitAsync(
        RepositoryInfo repository,
        bool amend)
    {
        string? comparisonTree = null;
        if (amend)
        {
            string revisionLine = await RunGitAsync(
                repository,
                "rev-list",
                "--parents",
                "-n",
                "1",
                "HEAD");
            string[] revisions = revisionLine.Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries);
            if (revisions.Length > 2)
            {
                return false;
            }

            comparisonTree = revisions.Length == 2
                ? revisions[1]
                : await CreateEmptyTreeAsync(repository);
        }

        List<string> arguments = ["diff", "--cached", "--quiet"];
        if (comparisonTree is not null)
        {
            arguments.Add(comparisonTree);
        }

        arguments.Add("--");
        GitCommandResult result = await _commandRunner.RunAsync(
            repository.Path,
            arguments,
            new GitCommandOptions(ThrowOnError: false));
        return result.ExitCode switch
        {
            0 => true,
            1 => false,
            _ => throw CreateCommandException(result)
        };
    }

    public async Task CherryPickAsync(
        RepositoryInfo repository,
        IReadOnlyList<GitCommit> commits,
        GitCherryPickOptions options)
    {
        ArgumentNullException.ThrowIfNull(commits);
        ArgumentNullException.ThrowIfNull(options);
        if (commits.Count == 0)
        {
            throw new ArgumentException("At least one commit is required.", nameof(commits));
        }

        GitCommit? mergeCommit = commits.FirstOrDefault(commit => commit.IsMerge);
        if (mergeCommit is not null)
        {
            if (commits.Count != 1)
            {
                throw new ArgumentException(
                    "A merge commit must be cherry-picked separately.",
                    nameof(commits));
            }

            if (options.MainlineParentNumber is not int parentNumber
                || parentNumber < 1
                || parentNumber > mergeCommit.ParentCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "A valid mainline parent is required for a merge commit.");
            }
        }
        else if (options.MainlineParentNumber is not null)
        {
            throw new ArgumentException(
                "A mainline parent can only be used with a merge commit.",
                nameof(options));
        }

        List<string> arguments = ["cherry-pick", "--no-edit"];
        if (options.AppendSourceReference)
        {
            arguments.Add("-x");
        }

        if (options.AddSignOff)
        {
            arguments.Add("--signoff");
        }

        if (options.NoCommit)
        {
            arguments.Add("--no-commit");
        }

        if (options.MainlineParentNumber is int mainlineParentNumber)
        {
            arguments.Add("--mainline");
            arguments.Add(mainlineParentNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        arguments.AddRange(commits.Select(commit => commit.Hash));
        _ = await _commandRunner.RunAsync(repository.Path, arguments);
    }

    private static void AddMessageArguments(List<string> args, string message)
    {
        var (title, body) = SplitMessage(message);
        args.Add("-m");
        args.Add(title);

        if (!string.IsNullOrWhiteSpace(body))
        {
            args.Add("-m");
            args.Add(body.Trim());
        }
    }

    private static void AddAllowEmptyArgument(
        List<string> arguments,
        GitCommitOptions options)
    {
        if (options.AllowEmpty)
        {
            arguments.Add("--allow-empty");
        }
    }

    private static (string Title, string? Body) SplitMessage(string message)
    {
        string normalizedMessage = message.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        string[] lines = normalizedMessage.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            string title = string.Join('\n', lines[..i]).Trim();
            string body = string.Join('\n', lines[(i + 1)..]).Trim();
            return (title, string.IsNullOrWhiteSpace(body) ? null : body);
        }

        return (normalizedMessage, null);
    }

    private async Task<string> RunGitAsync(RepositoryInfo repository, params string[] arguments)
    {
        GitCommandResult result = await _commandRunner.RunAsync(repository.Path, arguments);
        return result.StandardOutput.Trim();
    }

    private async Task<string> CreateEmptyTreeAsync(RepositoryInfo repository)
    {
        GitCommandResult result = await _commandRunner.RunAsync(
            repository.Path,
            ["mktree"],
            new GitCommandOptions(StandardInput: ""));
        return result.StandardOutput.Trim();
    }

    private static GitCommandException CreateCommandException(GitCommandResult result)
    {
        string message = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput.Trim()
            : result.StandardError.Trim();
        return new GitCommandException(message, result.ExitCode);
    }
}
