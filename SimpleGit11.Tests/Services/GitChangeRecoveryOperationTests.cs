using System.Collections.Generic;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitChangeRecoveryOperationTests
{
    [TestMethod]
    [DataRow(GitOperationKind.Rebase, "rebase")]
    [DataRow(GitOperationKind.CherryPick, "cherry-pick")]
    [DataRow(GitOperationKind.Revert, "revert")]
    public async Task ContinueOperationAsync_RunsMatchingCommandWithoutEditor(
        GitOperationKind operationKind,
        string expectedCommand)
    {
        RecordingGitCommandRunner runner = new();
        GitChangeRecoveryService service = new(runner);

        await service.ContinueOperationAsync(CreateRepository(), operationKind);

        CollectionAssert.AreEqual(
            new[] { expectedCommand, "--continue" },
            runner.Arguments.ToArray());
        Assert.AreEqual("true", runner.Options?.EnvironmentVariables?["GIT_EDITOR"]);
    }

    [TestMethod]
    [DataRow(GitOperationKind.Rebase, "rebase", "--skip")]
    [DataRow(GitOperationKind.Rebase, "rebase", "--abort")]
    [DataRow(GitOperationKind.CherryPick, "cherry-pick", "--skip")]
    [DataRow(GitOperationKind.CherryPick, "cherry-pick", "--abort")]
    [DataRow(GitOperationKind.Revert, "revert", "--skip")]
    [DataRow(GitOperationKind.Revert, "revert", "--abort")]
    public async Task OperationActionAsync_RunsMatchingCommand(
        GitOperationKind operationKind,
        string expectedCommand,
        string expectedAction)
    {
        RecordingGitCommandRunner runner = new();
        GitChangeRecoveryService service = new(runner);

        if (expectedAction == "--skip")
        {
            await service.SkipOperationAsync(CreateRepository(), operationKind);
        }
        else
        {
            await service.AbortOperationAsync(CreateRepository(), operationKind);
        }

        CollectionAssert.AreEqual(
            new[] { expectedCommand, expectedAction },
            runner.Arguments.ToArray());
    }

    private static RepositoryInfo CreateRepository()
    {
        return new RepositoryInfo(Environment.CurrentDirectory, "repository", "main");
    }

    private sealed class RecordingGitCommandRunner : IGitCommandRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public GitCommandOptions? Options { get; private set; }

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Arguments = arguments;
            Options = options;
            return Task.FromResult(new GitCommandResult(0, "", ""));
        }
    }
}
