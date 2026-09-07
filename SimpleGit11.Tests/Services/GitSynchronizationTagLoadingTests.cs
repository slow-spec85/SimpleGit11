using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitSynchronizationTagLoadingTests
{
    [TestMethod]
    public async Task Snapshot_ReadsBranchesAndBothTagListsConcurrentlyAndPreservesTagStates()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        ConcurrentReadRunner runner = new(timeout.Token);
        SynchronizationSnapshot snapshot = await ReadSnapshotAsync(runner, timeout.Token);

        Assert.AreEqual(3, runner.StartedReads);
        Assert.AreEqual(1, runner.RemoteRequests);
        Assert.HasCount(3, snapshot.Tags);
        Assert.IsFalse(snapshot.Tags.Single(tag => tag.Name == "published").NeedsSynchronization);
        Assert.IsTrue(snapshot.Tags.Single(tag => tag.Name == "conflicting").HasConflict);
        Assert.IsTrue(snapshot.Tags.Single(tag => tag.Name == "new").NeedsPublishing);
    }

    [TestMethod]
    public async Task Snapshot_RemoteFailureIsPropagatedInsteadOfShowingTagsAsUnpublished()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        ConcurrentReadRunner runner = new(timeout.Token) { FailRemote = true };
        await Assert.ThrowsAsync<GitCommandException>(() => ReadSnapshotAsync(runner, timeout.Token));
        Assert.AreEqual(3, runner.StartedReads);
    }

    [TestMethod]
    public async Task Snapshot_CancelsPendingRemoteTagRequest()
    {
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));
        ConcurrentReadRunner runner = new(cancellation.Token) { WaitForCancellation = true };
        Task<SynchronizationSnapshot> task = ReadSnapshotAsync(runner, cancellation.Token);
        await runner.AllReadsStarted.Task.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
    }

    private static Task<SynchronizationSnapshot> ReadSnapshotAsync(ConcurrentReadRunner runner, CancellationToken token)
    {
        GitRemoteService service = new(new GitTagService(runner), new GitConfigService(runner), runner);
        return service.GetConfiguredSynchronizationSnapshotAsync(
            new RepositoryInfo("C:/repo", "repo", "main"), new GitRemote("origin", ".", "."), token);
    }

    private sealed class ConcurrentReadRunner(CancellationToken lifetimeToken) : IGitCommandRunner
    {
        private int _startedReads;
        private int _remoteRequests;
        public int StartedReads => _startedReads;
        public int RemoteRequests => _remoteRequests;
        public bool FailRemote { get; init; }
        public bool WaitForCancellation { get; init; }
        public TaskCompletionSource AllReadsStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GitCommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments,
            GitCommandOptions? options = null, CancellationToken cancellationToken = default)
        {
            bool localBranches = arguments[0] == "for-each-ref"
                && arguments.Contains("refs/heads") && !arguments.Contains("refs/remotes");
            bool localTags = arguments[0] == "for-each-ref" && arguments.Contains("refs/tags");
            bool remoteTags = arguments[0] == "ls-remote";
            if (localBranches || localTags || remoteTags)
            {
                if (Interlocked.Increment(ref _startedReads) == 3)
                {
                    AllReadsStarted.TrySetResult();
                }
                // None of the independent reads can finish until all three have been started.
                await AllReadsStarted.Task.WaitAsync(lifetimeToken);
            }

            string output = "";
            if (remoteTags)
            {
                Interlocked.Increment(ref _remoteRequests);
                if (WaitForCancellation)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                if (FailRemote)
                {
                    throw new GitCommandException("Remote unavailable", 128);
                }
                output = "a\trefs/tags/published\nb\trefs/tags/conflicting\n";
            }
            else if (localTags)
            {
                output = string.Join('\n', new[] { "published", "conflicting", "new" }
                    .Select(name => string.Join('\x1f', name, "a", "", "commit", "", "")));
            }

            return new GitCommandResult(0, output, "");
        }
    }
}
