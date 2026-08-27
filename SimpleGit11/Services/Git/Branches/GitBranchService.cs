using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services;

public sealed class GitBranchService : IGitBranchService
{
    private const char UnitSeparator = '\x1f';
    private const string BranchFormat =
        "%(HEAD)\x1f%(refname:short)\x1f%(objectname:short)\x1f%(contents:subject)\x1f%(committerdate:local)\x1f%(symref)";
    private readonly IGitCommandRunner _commandRunner;

    public GitBranchService(IGitCommandRunner? commandRunner = null)
    {
        _commandRunner = commandRunner ?? new GitCommandRunner();
    }

    public async Task<IReadOnlyList<GitBranch>> GetLocalBranchesAsync(RepositoryInfo repository)
    {
        var output = await RunGitAsync(
            repository,
            "for-each-ref",
            $"--format={BranchFormat}",
            "refs/heads");

        return ParseBranches(output, false);
    }

    public async Task<IReadOnlyList<GitBranch>> GetRemoteBranchesAsync(RepositoryInfo repository)
    {
        var output = await RunGitAsync(
            repository,
            "branch",
            "-r",
            $"--format={BranchFormat}");

        return ParseBranches(output, true);
    }

    public Task CheckoutAsync(RepositoryInfo repository, GitBranch branch)
    {
        return RunGitAsync(repository, "checkout", branch.Name);
    }

    public Task CheckoutCommitAsync(RepositoryInfo repository, string commitHash)
    {
        return RunGitAsync(repository, "switch", "--detach", "--", commitHash);
    }

    public async Task<string> CheckoutRemoteAsync(RepositoryInfo repository, GitBranch branch)
    {
        var localBranchName = GetLocalNameFromRemoteBranch(branch);
        await RunGitAsync(repository, "checkout", "--track", "-b", localBranchName, branch.Name);
        return localBranchName;
    }

    public async Task<string> CreateLocalFromRemoteAsync(RepositoryInfo repository, GitBranch branch)
    {
        string localBranchName = GetLocalNameFromRemoteBranch(branch);
        await RunGitAsync(repository, "branch", "--track", localBranchName, branch.Name);
        return localBranchName;
    }

    public Task CreateBranchAsync(RepositoryInfo repository, string branchName, string startPointHash)
    {
        return RunGitAsync(repository, "branch", branchName, startPointHash);
    }

    public Task CreateAndCheckoutBranchAsync(RepositoryInfo repository, string branchName, string startPointHash)
    {
        return RunGitAsync(repository, "switch", "-c", branchName, startPointHash);
    }

    public Task CreateAndCheckoutOrphanBranchAsync(RepositoryInfo repository, string branchName)
    {
        return RunGitAsync(repository, "switch", "--orphan", branchName);
    }

    public async Task CreateOrphanBranchAsync(
        RepositoryInfo repository,
        string branchName,
        string initialCommitMessage)
    {
        string emptyTreeHash = (await RunGitWithInputAsync(repository, "", "mktree")).Trim();
        string commitHash = await CreateRootCommitAsync(repository, emptyTreeHash, initialCommitMessage);
        await RunGitAsync(repository, "branch", branchName, commitHash);
    }

    public async Task CreateOrphanBranchFromCommitAsync(
        RepositoryInfo repository,
        string branchName,
        string startPointHash,
        string initialCommitMessage,
        bool checkout)
    {
        string sourceTreeHash = (await RunGitAsync(
            repository,
            "rev-parse",
            $"{startPointHash}^{{tree}}")).Trim();
        string commitHash = await CreateRootCommitAsync(repository, sourceTreeHash, initialCommitMessage);
        await RunGitAsync(repository, "branch", branchName, commitHash);

        if (checkout)
        {
            await RunGitAsync(repository, "switch", branchName);
        }
    }

    public Task RenameBranchAsync(RepositoryInfo repository, GitBranch branch, string newBranchName)
    {
        return RunGitAsync(repository, "branch", "-m", branch.Name, newBranchName);
    }

    public Task DeleteBranchAsync(RepositoryInfo repository, GitBranch branch)
    {
        return RunGitAsync(repository, "branch", "-d", branch.Name);
    }

    public Task ForceDeleteBranchAsync(RepositoryInfo repository, GitBranch branch)
    {
        return RunGitAsync(repository, "branch", "-D", branch.Name);
    }

    public Task MergeAsync(
        RepositoryInfo repository,
        GitBranch branch,
        GitBranchMergeOptions options)
    {
        List<string> arguments = ["merge", options.Squash ? "--squash" : "--no-ff"];
        if (options.AllowUnrelatedHistories)
        {
            arguments.Add("--allow-unrelated-histories");
        }

        if (options.NoCommit || options.AllowUnrelatedHistories)
        {
            arguments.Add("--no-commit");
        }

        arguments.Add(branch.Name);
        return RunGitAsync(repository, [.. arguments]);
    }

    public Task PrepareSnapshotAsync(RepositoryInfo repository, GitBranch sourceBranch)
    {
        return RunGitAsync(repository, "read-tree", "--reset", "-u", sourceBranch.Name);
    }

    public async Task<GitBranchRebaseResult> RebaseAsync(
        RepositoryInfo repository,
        GitBranch branch)
    {
        string headBefore = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
        _ = await RunGitAsync(repository, "rebase", branch.Name);
        string headAfter = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
        return new GitBranchRebaseResult(
            !string.Equals(headBefore, headAfter, StringComparison.Ordinal));
    }

    public Task AbortMergeAsync(RepositoryInfo repository)
    {
        return RunGitAsync(repository, "merge", "--abort");
    }

    private async Task<string> RunGitAsync(RepositoryInfo repository, params string[] arguments)
    {
        return await RunGitProcessAsync(repository, null, arguments);
    }

    private async Task<string> CreateRootCommitAsync(
        RepositoryInfo repository,
        string treeHash,
        string commitMessage)
    {
        return (await RunGitAsync(
            repository,
            "commit-tree",
            treeHash,
            "-m",
            commitMessage)).Trim();
    }

    private async Task<string> RunGitWithInputAsync(
        RepositoryInfo repository,
        string standardInput,
        params string[] arguments)
    {
        return await RunGitProcessAsync(repository, standardInput, arguments);
    }

    private async Task<string> RunGitProcessAsync(
        RepositoryInfo repository,
        string? standardInput,
        IReadOnlyList<string> arguments)
    {
        GitCommandResult result = await _commandRunner.RunAsync(
            repository.Path,
            arguments,
            new GitCommandOptions(StandardInput: standardInput));
        return result.StandardOutput;
    }

    private static IReadOnlyList<GitBranch> ParseBranches(string output, bool isRemote)
    {
        var branches = new List<GitBranch>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = line.Split(UnitSeparator);
            if (fields.Length < 2)
            {
                continue;
            }

            bool isSymbolicReference = fields.Length > 5
                && !string.IsNullOrWhiteSpace(fields[5]);
            if (isRemote
                && (isSymbolicReference
                    || fields[1].EndsWith("/HEAD", StringComparison.Ordinal)))
            {
                continue;
            }

            branches.Add(new GitBranch(
                fields[1],
                fields[0] == "*",
                isRemote,
                fields.Length > 2 ? fields[2] : "",
                fields.Length > 3 ? fields[3] : "",
                fields.Length > 4 ? ParseDate(fields[4]) : null
            ));
        }

        return branches;
    }

    private static DateTime? ParseDate(string value)
    {
        return DateTime.ParseExact(
            value,
            "ddd MMM d HH:mm:ss yyyy",
            CultureInfo.InvariantCulture);
    }

    private static string GetLocalNameFromRemoteBranch(GitBranch branch)
    {
        var slashIndex = branch.Name.IndexOf('/');
        return slashIndex >= 0 && slashIndex < branch.Name.Length - 1
            ? branch.Name[(slashIndex + 1)..]
            : branch.Name;
    }

}
