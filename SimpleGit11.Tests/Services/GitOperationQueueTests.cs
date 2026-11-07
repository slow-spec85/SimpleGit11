using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitOperationQueueTests
{
    [TestMethod]
    public async Task WorkingTreeMayHaveChanged_RefreshesCountAfterCommitAndReset()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("tracked.txt", "original");
        await repository.CommitAllAsync();
        string initialHash = await repository.RunGitAsync("rev-parse", "HEAD");
        GitCommit initial = new(initialHash, initialHash[..7], "user", "", null, "initial", "");
        repository.WriteFile("tracked.txt", "modified");
        GitOperationQueue queue = new();
        GitStatusService status = new();
        GitChangeRecoveryService recovery = new();
        List<Task<int>> updates = [];
        queue.WorkingTreeMayHaveChanged += (_, _) =>
            updates.Add(status.GetChangedFileCountAsync(repository.Repository, CancellationToken.None));

        await queue.EnqueueAsync(() => repository.CommitAllAsync(), mayChangeWorkingTree: true);
        Assert.HasCount(1, updates);
        Assert.AreEqual(0, await updates[0]);

        await queue.EnqueueAsync(() => recovery.ResetAsync(repository.Repository, initial, "soft"),
            mayChangeWorkingTree: true);
        Assert.HasCount(2, updates);
        Assert.AreEqual(1, await updates[1]);

        await queue.EnqueueAsync(() => recovery.ResetAsync(repository.Repository, initial, "hard"),
            mayChangeWorkingTree: true);
        Assert.HasCount(3, updates);
        Assert.AreEqual(0, await updates[2]);
    }

    [TestMethod]
    public async Task WorkingTreeMayHaveChanged_AlsoNotifiesAfterFailureButSkipsReads()
    {
        GitOperationQueue queue = new();
        int updates = 0;
        queue.WorkingTreeMayHaveChanged += (_, _) =>
        {
            Assert.IsFalse(queue.IsRunning);
            updates++;
        };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => queue.EnqueueAsync(
            () => throw new InvalidOperationException(), mayChangeWorkingTree: true));
        await queue.EnqueueAsync(() => Task.CompletedTask);

        Assert.AreEqual(1, updates);
    }
}
