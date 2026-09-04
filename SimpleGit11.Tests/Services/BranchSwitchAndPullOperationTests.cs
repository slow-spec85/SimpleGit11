using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class BranchSwitchAndPullOperationTests
{
    [TestMethod]
    public async Task RunWhenConfirmedAsync_ConfirmationDeclined_DoesNotRunOperation()
    {
        int operationCount = 0;

        await BranchSwitchAndPullOperation.RunWhenConfirmedAsync(
            () => Task.FromResult(false),
            () =>
            {
                operationCount++;
                return Task.CompletedTask;
            });

        Assert.AreEqual(0, operationCount);
    }

    [TestMethod]
    public async Task RunWhenConfirmedAsync_ConfirmationAccepted_RunsOperationOnce()
    {
        int operationCount = 0;

        await BranchSwitchAndPullOperation.RunWhenConfirmedAsync(
            () => Task.FromResult(true),
            () =>
            {
                operationCount++;
                return Task.CompletedTask;
            });

        Assert.AreEqual(1, operationCount);
    }

    [TestMethod]
    public async Task ExecuteAsync_BranchWithUpstream_SwitchesBeforePullingConfiguredUpstream()
    {
        RecordingGitCommandRunner runner = new();

        List<string> switchedBranches = [];
        await ExecuteAsync(runner, CreateBranch("origin/feature"), branchName =>
        {
            Assert.HasCount(1, runner.Commands);
            switchedBranches.Add(branchName);
        });

        CollectionAssert.AreEqual(new[] { "feature" }, switchedBranches);

        AssertCommands(
            runner,
            ["switch", "--", "feature"],
            ["pull", "--progress"]);
    }

    [TestMethod]
    public async Task ExecuteAsync_BranchWithoutUpstream_SwitchesBeforePullingSelectedRemoteBranch()
    {
        RecordingGitCommandRunner runner = new();

        await ExecuteAsync(runner, CreateBranch(""));

        AssertCommands(
            runner,
            ["switch", "--", "feature"],
            ["pull", "--progress", "backup", "refs/heads/feature"]);
    }

    [TestMethod]
    public async Task ExecuteAsync_SwitchFails_DoesNotPull()
    {
        RecordingGitCommandRunner runner = new(failingInvocation: 1);
        List<string> switchedBranches = [];

        await Assert.ThrowsExactlyAsync<GitCommandException>(
            () => ExecuteAsync(runner, CreateBranch("origin/feature"), switchedBranches.Add));

        Assert.IsEmpty(switchedBranches);
        Assert.HasCount(1, runner.Commands);
        CollectionAssert.AreEqual(
            new[] { "switch", "--", "feature" },
            runner.Commands[0].ToArray());
    }

    [TestMethod]
    public async Task ExecuteAsync_PullFails_StillNotifiesAboutSuccessfulSwitch()
    {
        RecordingGitCommandRunner runner = new(failingInvocation: 2);
        List<string> switchedBranches = [];

        await Assert.ThrowsExactlyAsync<GitCommandException>(
            () => ExecuteAsync(runner, CreateBranch("origin/feature"), switchedBranches.Add));

        CollectionAssert.AreEqual(new[] { "feature" }, switchedBranches);
        Assert.HasCount(2, runner.Commands);
    }

    private static Task<GitRemoteOperationResult> ExecuteAsync(
        RecordingGitCommandRunner runner,
        BranchSynchronizationItem branch,
        Action<string>? onBranchSwitched = null)
    {
        GitBranchService branchService = new(runner);
        GitRemoteService remoteService = new(
            new GitTagService(runner),
            new GitConfigService(runner),
            runner);

        return BranchSwitchAndPullOperation.ExecuteAsync(
            branchService,
            remoteService,
            new RepositoryInfo("C:\\repository", "repository", "main"),
            branch,
            new GitRemote("backup", "https://example.test/repository.git", ""),
            onBranchSwitched ?? (_ => { }),
            CancellationToken.None);
    }

    private static BranchSynchronizationItem CreateBranch(string upstreamBranch)
    {
        return new BranchSynchronizationItem(
            name: "feature",
            isCurrent: false,
            upstreamBranch: upstreamBranch,
            upstreamRemoteName: upstreamBranch.Length > 0 ? "origin" : "",
            upstreamTrackingState: "<",
            pushRemoteName: "origin",
            explicitPushRemoteName: "",
            pushTrackingBranch: "origin/feature",
            pushTrackingState: "=",
            pushAheadCount: 0,
            pushBehindCount: 0,
            hasPushRemoteOverride: false,
            remoteTrackingBranch: "origin/feature",
            tracksSelectedRemote: true,
            isPublishedToRemote: true,
            aheadCount: 0,
            behindCount: 1);
    }

    private static void AssertCommands(
        RecordingGitCommandRunner runner,
        params IReadOnlyList<string>[] expectedCommands)
    {
        Assert.AreEqual(expectedCommands.Length, runner.Commands.Count);
        for (int index = 0; index < expectedCommands.Length; index++)
        {
            CollectionAssert.AreEqual(
                expectedCommands[index].ToArray(),
                runner.Commands[index].ToArray());
        }
    }

    private sealed class RecordingGitCommandRunner(int? failingInvocation = null)
        : IGitCommandRunner
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(arguments.ToArray());
            if (Commands.Count == failingInvocation)
            {
                throw new GitCommandException("switch failed", 1);
            }

            return Task.FromResult(new GitCommandResult(0, "", ""));
        }
    }
}
