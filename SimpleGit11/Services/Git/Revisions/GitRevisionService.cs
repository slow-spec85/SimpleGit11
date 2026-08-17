using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Services;

public sealed class GitRevisionService : IGitRevisionService
{
    private const char RecordSeparator = '\x1e';
    private const char UnitSeparator = '\x1f';
    private const int CommitSuggestionCount = 100;
    private readonly IGitCommandRunner _commandRunner;

    public GitRevisionService(IGitCommandRunner? commandRunner = null)
    {
        _commandRunner = commandRunner ?? new GitCommandRunner();
    }

    public Task<IReadOnlyList<GitRevisionSuggestion>> GetSuggestionsAsync(
        RepositoryInfo repository,
        GitRevisionKind kind,
        CancellationToken cancellationToken)
    {
        return kind switch
        {
            GitRevisionKind.Branch => GetBranchesAsync(repository, cancellationToken),
            GitRevisionKind.Tag => GetTagsAsync(repository, cancellationToken),
            GitRevisionKind.Commit => GetCommitsAsync(repository, cancellationToken),
            _ => Task.FromResult<IReadOnlyList<GitRevisionSuggestion>>([])
        };
    }

    public async Task<GitResolvedRevision> ResolveAsync(
        RepositoryInfo repository,
        GitRevisionKind kind,
        string value,
        CancellationToken cancellationToken)
    {
        string startPoint = value.Trim();
        if (string.IsNullOrWhiteSpace(startPoint))
        {
            throw new GitCommandException("The Git revision is empty.", -1);
        }

        string commitHash = kind switch
        {
            GitRevisionKind.Head => await ResolveRevisionAsync(repository, "HEAD", cancellationToken),
            GitRevisionKind.Branch => await ResolveBranchAsync(repository, startPoint, cancellationToken),
            GitRevisionKind.Tag => await ResolveRevisionAsync(
                repository,
                startPoint.StartsWith("refs/tags/", StringComparison.Ordinal)
                    ? startPoint
                    : $"refs/tags/{startPoint}",
                cancellationToken),
            _ => await ResolveRevisionAsync(repository, startPoint, cancellationToken)
        };
        string shortHash = await RunGitAsync(
            repository,
            ["rev-parse", "--short", commitHash],
            cancellationToken);
        return new GitResolvedRevision(commitHash, shortHash);
    }

    private async Task<string> ResolveBranchAsync(
        RepositoryInfo repository,
        string value,
        CancellationToken cancellationToken)
    {
        if (value.StartsWith("refs/heads/", StringComparison.Ordinal)
            || value.StartsWith("refs/remotes/", StringComparison.Ordinal))
        {
            return await ResolveRevisionAsync(repository, value, cancellationToken);
        }

        GitCommandException? localBranchException = null;
        try
        {
            return await ResolveRevisionAsync(repository, $"refs/heads/{value}", cancellationToken);
        }
        catch (GitCommandException exception) when (exception.ExitCode is 1 or 128)
        {
            localBranchException = exception;
        }

        try
        {
            return await ResolveRevisionAsync(repository, $"refs/remotes/{value}", cancellationToken);
        }
        catch (GitCommandException exception) when (exception.ExitCode is 1 or 128)
        {
            throw new GitCommandException(
                string.IsNullOrWhiteSpace(exception.Message)
                    ? localBranchException?.Message ?? "The branch was not found."
                    : exception.Message,
                exception.ExitCode);
        }
    }

    private async Task<IReadOnlyList<GitRevisionSuggestion>> GetBranchesAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        string format =
            $"%(refname:short){UnitSeparator}%(objectname:short){UnitSeparator}%(contents:subject){UnitSeparator}%(symref){UnitSeparator}%(refname)";
        string output = await RunGitAsync(
            repository,
            ["for-each-ref", "--sort=-committerdate", $"--format={format}", "refs/heads", "refs/remotes"],
            cancellationToken);

        List<GitRevisionSuggestion> result = [];
        foreach (string record in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.TrimEnd('\r').Split(UnitSeparator);
            if (fields.Length < 5 || !string.IsNullOrWhiteSpace(fields[3]))
            {
                continue;
            }

            result.Add(new GitRevisionSuggestion(
                fields[0],
                fields[0],
                JoinDescription(fields[1], fields[2]),
                fields[1],
                fields[4].StartsWith("refs/remotes/", StringComparison.Ordinal)));
        }

        return result;
    }

    private async Task<IReadOnlyList<GitRevisionSuggestion>> GetTagsAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        string format =
            $"%(refname:short){UnitSeparator}%(objectname:short){UnitSeparator}%(*objectname:short){UnitSeparator}%(contents:subject)";
        string output = await RunGitAsync(
            repository,
            ["for-each-ref", "--sort=-creatordate", $"--format={format}", "refs/tags"],
            cancellationToken);

        List<GitRevisionSuggestion> result = [];
        foreach (string record in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.TrimEnd('\r').Split(UnitSeparator);
            if (fields.Length < 4)
            {
                continue;
            }

            string hash = string.IsNullOrWhiteSpace(fields[2]) ? fields[1] : fields[2];
            result.Add(new GitRevisionSuggestion(
                fields[0],
                fields[0],
                JoinDescription(hash, fields[3]),
                hash));
        }

        return result;
    }

    private async Task<IReadOnlyList<GitRevisionSuggestion>> GetCommitsAsync(
        RepositoryInfo repository,
        CancellationToken cancellationToken)
    {
        string format =
            $"%H{UnitSeparator}%h{UnitSeparator}%ad{UnitSeparator}%s{RecordSeparator}";
        string output = await RunGitAsync(
            repository,
            [
                "log",
                $"--max-count={CommitSuggestionCount}",
                "--date=short",
                $"--pretty=format:{format}"
            ],
            cancellationToken);

        List<GitRevisionSuggestion> result = [];
        foreach (string record in output.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.Trim('\r', '\n').Split(UnitSeparator);
            if (fields.Length < 4)
            {
                continue;
            }

            result.Add(new GitRevisionSuggestion(
                fields[0],
                JoinDescription(fields[1], fields[3]),
                fields[2],
                fields[1]));
        }

        return result;
    }

    private Task<string> ResolveRevisionAsync(
        RepositoryInfo repository,
        string revision,
        CancellationToken cancellationToken)
    {
        return RunGitAsync(
            repository,
            ["rev-parse", "--verify", "--end-of-options", $"{revision}^{{commit}}"],
            cancellationToken);
    }

    private async Task<string> RunGitAsync(
        RepositoryInfo repository,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await _commandRunner.RunAsync(
            repository.Path,
            arguments,
            cancellationToken: cancellationToken);
        return result.StandardOutput.Trim();
    }

    private static string JoinDescription(string first, string second)
    {
        return string.IsNullOrWhiteSpace(second) ? first : $"{first}   {second.Trim()}";
    }
}
