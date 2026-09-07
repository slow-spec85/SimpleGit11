using System.Collections.Concurrent;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitSynchronizationPerformanceTests
{
    [TestMethod]
    public async Task Snapshot_EqualHashesAndUnpublishedBranchesDoNotRunRevisionComparisons()
    {
        SnapshotRunner runner = new([new("equal", "a", "a"), new("unpublished", "b", null)]);
        SynchronizationSnapshot snapshot = await ReadSnapshotAsync(runner);

        Assert.IsEmpty(runner.Comparisons);
        BranchSynchronizationItem equal = snapshot.Branches.Single(branch => branch.Name == "equal");
        Assert.IsTrue(equal.IsPublishedToRemote);
        Assert.IsFalse(equal.NeedsSynchronization);
        Assert.IsTrue(snapshot.Branches.Single(branch => branch.Name == "unpublished").CanPush);
    }

    [TestMethod]
    public async Task Snapshot_ReusesCountsAcrossBranchesAndPullPushTargetsWithIdenticalCommits()
    {
        SnapshotRunner runner = new([new("first", "a", "b"), new("second", "a", "b")]);
        SynchronizationSnapshot snapshot = await ReadSnapshotAsync(runner);

        CollectionAssert.AreEqual(new[] { "a...b" }, runner.Comparisons.ToArray());
        Assert.HasCount(2, snapshot.OutgoingBranches);
        Assert.HasCount(2, snapshot.IncomingBranches);
        foreach (BranchSynchronizationItem branch in snapshot.Branches)
        {
            Assert.AreEqual(2, branch.AheadCount);
            Assert.AreEqual(3, branch.BehindCount);
        }
    }

    [TestMethod]
    public async Task Snapshot_DistinctComparisonsRunConcurrentlyWithBoundedFanOut()
    {
        SnapshotRunner runner = new(Enumerable.Range(0, 12)
            .Select(index => new BranchData($"branch{index:D2}", $"local{index}", $"remote{index}"))
            .ToArray(), releaseAfter: 4);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        SynchronizationSnapshot snapshot = await ReadSnapshotAsync(runner, timeout.Token);

        Assert.AreEqual(4, runner.MaximumActive);
        Assert.AreEqual(0, runner.Active);
        Assert.HasCount(12, runner.Comparisons);
        CollectionAssert.AreEqual(runner.Branches.Select(branch => branch.Name).ToArray(),
            snapshot.Branches.Select(branch => branch.Name).ToArray());
    }

    [TestMethod]
    public async Task Snapshot_CancellationStopsInFlightComparisons()
    {
        SnapshotRunner runner = new([new("first", "a", "b")], releaseAfter: int.MaxValue);
        using CancellationTokenSource cancellation = new();
        Task<SynchronizationSnapshot> task = ReadSnapshotAsync(runner, cancellation.Token);
        await runner.FirstComparison.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
        Assert.AreEqual(0, runner.Active);
    }

    [TestMethod]
    public async Task Snapshot_FailedComparisonIsNotReportedAsSynchronized()
    {
        SnapshotRunner runner = new([new("first", "a", "b")]) { FailComparison = true };
        await Assert.ThrowsAsync<GitCommandException>(() => ReadSnapshotAsync(runner));
    }

    private static Task<SynchronizationSnapshot> ReadSnapshotAsync(SnapshotRunner runner, CancellationToken token = default)
    {
        GitRemoteService service = new(new GitTagService(runner), new GitConfigService(runner), runner);
        return service.GetLocalConfiguredSynchronizationSnapshotAsync(
            new RepositoryInfo("C:/repo", "repo", "main"), new GitRemote("origin", ".", "."), [], token);
    }

    private sealed record BranchData(string Name, string LocalHash, string? RemoteHash);

    private sealed class SnapshotRunner(BranchData[] branches, int releaseAfter = 0) : IGitCommandRunner
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private int _maximumActive;
        public BranchData[] Branches => branches;
        public ConcurrentQueue<string> Comparisons { get; } = new();
        public TaskCompletionSource FirstComparison { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FailComparison { get; init; }
        public int Active => _active;
        public int MaximumActive => _maximumActive;

        public async Task<GitCommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments,
            GitCommandOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string output;
            if (arguments[0] == "config" || arguments.Contains("refs/tags"))
            {
                output = "";
            }
            else if (arguments[0] == "for-each-ref" && arguments.Contains("refs/remotes"))
            {
                output = string.Join('\n', branches.SelectMany(branch => branch.RemoteHash is null
                    ? new[] { $"refs/heads/{branch.Name}\x1f{branch.LocalHash}" }
                    : new[] { $"refs/heads/{branch.Name}\x1f{branch.LocalHash}", $"refs/remotes/origin/{branch.Name}\x1f{branch.RemoteHash}" }));
            }
            else if (arguments[0] == "for-each-ref")
            {
                output = string.Join('\n', branches.Select(branch => string.Join('\x1f',
                    branch.Name, "", $"origin/{branch.Name}", "origin", "", "origin", $"origin/{branch.Name}", "", "")));
            }
            else if (arguments[0] == "rev-list")
            {
                int active = Interlocked.Increment(ref _active);
                lock (Comparisons)
                {
                    _maximumActive = Math.Max(_maximumActive, active);
                    Comparisons.Enqueue(arguments[^1]);
                    if (Comparisons.Count >= releaseAfter)
                    {
                        _release.TrySetResult();
                    }
                }
                FirstComparison.TrySetResult();
                try
                {
                    await _release.Task.WaitAsync(cancellationToken);
                    if (FailComparison)
                    {
                        throw new GitCommandException("Comparison failed", 128);
                    }
                    output = "2\t3";
                }
                finally
                {
                    Interlocked.Decrement(ref _active);
                }
            }
            else
            {
                throw new AssertFailedException(string.Join(' ', arguments));
            }

            return new GitCommandResult(0, output, "");
        }
    }
}
