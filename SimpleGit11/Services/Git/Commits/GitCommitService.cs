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
    public Task<string> CommitAsync(RepositoryInfo repository, string message)
    {
        List<string> args = ["commit"];
        AddMessageArguments(args, message);
        return RunGitAsync(repository, [.. args]);
    }

    public Task<string> AmendAsync(RepositoryInfo repository, string? message)
    {
        List<string> args = ["commit", "--amend"];
        if (!string.IsNullOrWhiteSpace(message))
        {
            AddMessageArguments(args, message);
        }
        else
        {
            args.Add("--no-edit");
        }

        return RunGitAsync(repository, [.. args]);
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

    private async Task<string> RunGitAsync(RepositoryInfo repository, string[] arguments)
    {
        GitCommandResult result = await _commandRunner.RunAsync(repository.Path, arguments);
        return result.StandardOutput.Trim();
    }
}
