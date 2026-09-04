using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitRemoteServiceTests
{
    [TestMethod]
    public async Task SynchronizationSnapshot_TracksWorktreeOccupancyAndReleasesBranchAfterRemoval()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        using TemporaryDirectory linkedDirectory = new();
        string worktreePath = linkedDirectory.GetPath("linked worktree");
        repository.WriteFile("tracked.txt", "initial");
        await repository.CommitAllAsync();
        await repository.RunGitAsync("remote", "add", "origin", ".");
        await repository.RunGitAsync("branch", "available");
        await repository.RunGitAsync("worktree", "add", "-b", "occupied", worktreePath);

        GitRemoteService service = new(new GitTagService(), new GitConfigService());
        GitRemote remote = new("origin", ".", ".");
        SynchronizationSnapshot snapshot = await service.GetLocalConfiguredSynchronizationSnapshotAsync(
            repository.Repository, remote, []);

        BranchSynchronizationItem occupied = snapshot.Branches.Single(branch => branch.Name == "occupied");
        Assert.IsTrue(occupied.IsInOtherWorktree);
        Assert.AreEqual(worktreePath.Replace('\\', '/'), occupied.WorktreePath.Replace('\\', '/'));
        Assert.IsFalse(snapshot.Branches.Single(branch => branch.Name == "available").IsInOtherWorktree);
        Assert.IsFalse(snapshot.CurrentBranch!.IsInOtherWorktree);

        RepositoryInfo linkedRepository = new(worktreePath, "linked", "occupied");
        SynchronizationSnapshot linkedSnapshot = await service.GetLocalConfiguredSynchronizationSnapshotAsync(
            linkedRepository, remote, []);
        Assert.IsFalse(linkedSnapshot.CurrentBranch!.IsInOtherWorktree);
        Assert.IsTrue(linkedSnapshot.Branches.Single(branch => branch.Name == "main").IsInOtherWorktree);

        SynchronizationSnapshot selectedRemoteSnapshot = await service.GetLocalSynchronizationSnapshotAsync(
            repository.Repository, remote, []);
        Assert.IsTrue(selectedRemoteSnapshot.Branches.Single(branch => branch.Name == "occupied").IsInOtherWorktree);

        await repository.RunGitAsync("worktree", "remove", worktreePath);
        snapshot = await service.GetLocalConfiguredSynchronizationSnapshotAsync(repository.Repository, remote, []);
        Assert.IsFalse(snapshot.Branches.Single(branch => branch.Name == "occupied").IsInOtherWorktree);
    }

    [TestMethod]
    public async Task GetCurrentBranchRemoteStatusAsync_SeparatesUpstreamAndPushDefault()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("tracked.txt", "initial");
        await repository.CommitAllAsync();
        await repository.RunGitAsync("branch", "-M", "main");
        await repository.RunGitAsync("remote", "add", "origin", ".");
        await repository.RunGitAsync("remote", "add", "public", ".");
        await repository.RunGitAsync("update-ref", "refs/remotes/origin/main", "HEAD");
        await repository.RunGitAsync("update-ref", "refs/remotes/public/main", "HEAD");
        await repository.RunGitAsync("branch", "--set-upstream-to=origin/main", "main");
        await repository.RunGitAsync("config", "--local", "remote.pushDefault", "public");

        GitRemoteService service = new(new GitTagService(), new GitConfigService());
        GitRemote activeRemote = new("origin", ".", ".");

        GitCurrentBranchRemoteStatus status = await service.GetCurrentBranchRemoteStatusAsync(
            repository.Repository,
            activeRemote);

        Assert.IsTrue(status.HasConfiguredUpstream);
        Assert.IsNotNull(status.TrackingTarget);
        Assert.AreEqual("origin", status.TrackingTarget.RemoteName);
        Assert.AreEqual("origin/main", status.TrackingTarget.TrackingBranch);
        Assert.IsTrue(status.TrackingTarget.IsPublished);
        Assert.IsNotNull(status.PushTarget);
        Assert.AreEqual("public", status.PushTarget.RemoteName);
        Assert.AreEqual("public/main", status.PushTarget.TrackingBranch);
        Assert.IsTrue(status.PushTarget.IsPublished);
    }

    [TestMethod]
    public async Task GetCurrentBranchRemoteStatusAsync_UsesSelectedRemoteOnlyAsTrackingFallback()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("tracked.txt", "initial");
        await repository.CommitAllAsync();
        await repository.RunGitAsync("branch", "-M", "main");
        await repository.RunGitAsync("remote", "add", "backup", ".");
        await repository.RunGitAsync("update-ref", "refs/remotes/backup/main", "HEAD");

        GitRemoteService service = new(new GitTagService(), new GitConfigService());
        GitRemote activeRemote = new("backup", ".", ".");

        GitCurrentBranchRemoteStatus status = await service.GetCurrentBranchRemoteStatusAsync(
            repository.Repository,
            activeRemote);

        Assert.IsFalse(status.HasConfiguredUpstream);
        Assert.IsNotNull(status.TrackingTarget);
        Assert.AreEqual("backup/main", status.TrackingTarget.TrackingBranch);
        Assert.IsNull(status.PushTarget);
    }

    [TestMethod]
    public async Task GetCurrentBranchRemoteStatusAsync_BranchPushRemoteOverridesPushDefault()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("tracked.txt", "initial");
        await repository.CommitAllAsync();
        await repository.RunGitAsync("branch", "-M", "main");
        await repository.RunGitAsync("remote", "add", "origin", ".");
        await repository.RunGitAsync("remote", "add", "public", ".");
        await repository.RunGitAsync("remote", "add", "backup", ".");
        await repository.RunGitAsync("update-ref", "refs/remotes/origin/main", "HEAD");
        await repository.RunGitAsync("update-ref", "refs/remotes/backup/main", "HEAD");
        await repository.RunGitAsync("branch", "--set-upstream-to=origin/main", "main");
        await repository.RunGitAsync("config", "--local", "remote.pushDefault", "public");
        await repository.RunGitAsync("config", "--local", "branch.main.pushRemote", "backup");

        GitRemoteService service = new(new GitTagService(), new GitConfigService());
        GitRemote activeRemote = new("origin", ".", ".");

        GitCurrentBranchRemoteStatus status = await service.GetCurrentBranchRemoteStatusAsync(
            repository.Repository,
            activeRemote);

        Assert.IsNotNull(status.PushTarget);
        Assert.AreEqual("backup", status.PushTarget.RemoteName);
        Assert.AreEqual("backup/main", status.PushTarget.TrackingBranch);
    }

    [TestMethod]
    public async Task GetCurrentBranchRemoteStatusAsync_PreservesUnpublishedExplicitPushTarget()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("tracked.txt", "initial");
        await repository.CommitAllAsync();
        await repository.RunGitAsync("branch", "-M", "main");
        await repository.RunGitAsync("remote", "add", "origin", ".");
        await repository.RunGitAsync("config", "--local", "branch.main.pushRemote", "origin");

        GitRemoteService service = new(new GitTagService(), new GitConfigService());
        GitRemote activeRemote = new("origin", ".", ".");

        GitCurrentBranchRemoteStatus status = await service.GetCurrentBranchRemoteStatusAsync(
            repository.Repository,
            activeRemote);

        Assert.IsNotNull(status.PushTarget);
        Assert.AreEqual("origin/main", status.PushTarget.TrackingBranch);
        Assert.IsFalse(status.PushTarget.IsPublished);
        Assert.AreEqual(0, status.PushTarget.AheadCount);
        Assert.AreEqual(0, status.PushTarget.BehindCount);
    }

    [TestMethod]
    public async Task GetCurrentBranchRemoteStatusAsync_PreservesMissingConfiguredUpstream()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("tracked.txt", "initial");
        await repository.CommitAllAsync();
        await repository.RunGitAsync("branch", "-M", "main");
        await repository.RunGitAsync("remote", "add", "origin", ".");
        await repository.RunGitAsync("config", "--local", "branch.main.remote", "origin");
        await repository.RunGitAsync(
            "config",
            "--local",
            "branch.main.merge",
            "refs/heads/release");

        GitRemoteService service = new(new GitTagService(), new GitConfigService());
        GitRemote activeRemote = new("origin", ".", ".");

        GitCurrentBranchRemoteStatus status = await service.GetCurrentBranchRemoteStatusAsync(
            repository.Repository,
            activeRemote);

        Assert.IsTrue(status.HasConfiguredUpstream);
        Assert.IsNotNull(status.TrackingTarget);
        Assert.AreEqual("origin/release", status.TrackingTarget.TrackingBranch);
        Assert.IsFalse(status.TrackingTarget.IsPublished);
    }

    [TestMethod]
    public async Task GetComparisonCommitsPageAsync_SetsStructuredRangeSide()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("base.txt", "base");
        await repository.CommitAllAsync("base");
        await repository.RunGitAsync("branch", "right");
        repository.WriteFile("left.txt", "left");
        await repository.CommitAllAsync("left");
        await repository.RunGitAsync("switch", "right");
        repository.WriteFile("right.txt", "right");
        await repository.CommitAllAsync("right");
        GitRemoteService service = new(new GitTagService(), new GitConfigService());

        GitCommitPage page = await service.GetComparisonCommitsPageAsync(
            repository.Repository,
            "main",
            "right",
            "main",
            "right",
            0,
            300);

        Assert.HasCount(2, page.Commits);
        Assert.IsFalse(page.HasMore);
        Assert.AreEqual(GitCommitRangeSide.Left, page.Commits.Single(commit => commit.Title == "left").RangeSide);
        Assert.AreEqual(GitCommitRangeSide.Right, page.Commits.Single(commit => commit.Title == "right").RangeSide);
    }

    [TestMethod]
    public async Task GetCommitsPageAsync_ReturnsConsecutiveRangePages()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        for (int index = 1; index <= 5; index++)
        {
            repository.WriteFile($"range-{index}.txt", index.ToString());
            await repository.CommitAllAsync($"range commit {index}");
        }

        GitRemoteService service = new(new GitTagService(), new GitConfigService());

        GitCommitPage firstPage = await service.GetCommitsPageAsync(
            repository.Repository,
            "HEAD",
            0,
            2);
        GitCommitPage secondPage = await service.GetCommitsPageAsync(
            repository.Repository,
            "HEAD",
            2,
            2);
        GitCommitPage lastPage = await service.GetCommitsPageAsync(
            repository.Repository,
            "HEAD",
            4,
            2);

        Assert.HasCount(2, firstPage.Commits);
        Assert.AreEqual("range commit 5", firstPage.Commits[0].Title);
        Assert.IsNull(firstPage.Commits[0].IsSynchronized);
        Assert.IsFalse(firstPage.Commits[0].NeedsSynchronization);
        Assert.IsTrue(firstPage.HasMore);
        Assert.HasCount(2, secondPage.Commits);
        Assert.AreEqual("range commit 3", secondPage.Commits[0].Title);
        Assert.IsTrue(secondPage.HasMore);
        Assert.HasCount(1, lastPage.Commits);
        Assert.AreEqual("range commit 1", lastPage.Commits[0].Title);
        Assert.IsFalse(lastPage.HasMore);
    }

    [TestMethod]
    public async Task GetComparisonCommitsPageAsync_PreservesRangeSidesAcrossPages()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("base.txt", "base");
        await repository.CommitAllAsync("base");
        await repository.RunGitAsync("branch", "right");
        for (int index = 1; index <= 2; index++)
        {
            repository.WriteFile($"left-{index}.txt", index.ToString());
            await repository.CommitAllAsync($"left {index}");
        }

        await repository.RunGitAsync("switch", "right");
        for (int index = 1; index <= 2; index++)
        {
            repository.WriteFile($"right-{index}.txt", index.ToString());
            await repository.CommitAllAsync($"right {index}");
        }

        GitRemoteService service = new(new GitTagService(), new GitConfigService());

        GitCommitPage firstPage = await service.GetComparisonCommitsPageAsync(
            repository.Repository,
            "main",
            "right",
            "main",
            "right",
            0,
            2);
        GitCommitPage lastPage = await service.GetComparisonCommitsPageAsync(
            repository.Repository,
            "main",
            "right",
            "main",
            "right",
            2,
            2);
        IReadOnlyList<GitCommit> commits = firstPage.Commits.Concat(lastPage.Commits).ToList();

        Assert.IsTrue(firstPage.HasMore);
        Assert.IsFalse(lastPage.HasMore);
        Assert.HasCount(4, commits);
        Assert.AreEqual(4, commits.Select(commit => commit.Hash).Distinct().Count());
        Assert.AreEqual(2, commits.Count(commit => commit.RangeSide == GitCommitRangeSide.Left));
        Assert.AreEqual(2, commits.Count(commit => commit.RangeSide == GitCommitRangeSide.Right));
    }

    [TestMethod]
    public async Task GetLocalConfiguredSynchronizationSnapshotAsync_UsesUpstreamAndPushDefault()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("tracked.txt", "initial");
        await repository.CommitAllAsync();
        await repository.RunGitAsync("branch", "-M", "main");
        await repository.RunGitAsync("remote", "add", "origin", ".");
        await repository.RunGitAsync("remote", "add", "public", ".");
        await repository.RunGitAsync("update-ref", "refs/remotes/origin/main", "HEAD");
        await repository.RunGitAsync("update-ref", "refs/remotes/public/main", "HEAD");
        await repository.RunGitAsync("branch", "--set-upstream-to=origin/main", "main");
        await repository.RunGitAsync("config", "--local", "remote.pushDefault", "public");

        GitRemoteService service = new(new GitTagService(), new GitConfigService());
        GitRemote activeRemote = new("origin", ".", ".");

        SynchronizationSnapshot snapshot =
            await service.GetLocalConfiguredSynchronizationSnapshotAsync(
                repository.Repository,
                activeRemote,
                []);

        BranchSynchronizationItem branch = snapshot.CurrentBranch!;
        Assert.AreEqual("origin/main", branch.RemoteTrackingBranch);
        Assert.AreEqual("public", branch.ConfiguredPushRemoteName);
        Assert.AreEqual("public/main", branch.PushTrackingBranch);
        Assert.IsTrue(branch.HasPushRemoteOverride);
        Assert.IsFalse(branch.CanPush);

        repository.WriteFile("tracked.txt", "changed");
        await repository.CommitAllAsync("local change");

        snapshot = await service.GetLocalConfiguredSynchronizationSnapshotAsync(
            repository.Repository,
            activeRemote,
            []);

        branch = snapshot.CurrentBranch!;
        Assert.IsTrue(branch.CanPush);
        Assert.IsTrue(branch.HasOutgoingToPushRemote);
        Assert.AreEqual(1, branch.PushAheadCount);
        Assert.AreEqual(0, branch.PushBehindCount);
        Assert.IsFalse(branch.HasIncomingCommits);
    }

    [TestMethod]
    public async Task GetLocalConfiguredSynchronizationSnapshotAsync_DivergedPushTargetRequiresForceWithLease()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("tracked.txt", "initial");
        await repository.CommitAllAsync();
        await repository.RunGitAsync("branch", "-M", "main");
        string commonCommit = await repository.RunGitAsync("rev-parse", "HEAD");
        await repository.RunGitAsync("remote", "add", "public", ".");
        await repository.RunGitAsync("config", "--local", "remote.pushDefault", "public");

        repository.WriteFile("local.txt", "local");
        await repository.CommitAllAsync("local change");
        await repository.RunGitAsync("checkout", "--detach", commonCommit);
        repository.WriteFile("remote.txt", "remote");
        await repository.CommitAllAsync("remote change");
        string remoteCommit = await repository.RunGitAsync("rev-parse", "HEAD");
        await repository.RunGitAsync("checkout", "main");
        await repository.RunGitAsync("update-ref", "refs/remotes/public/main", remoteCommit);

        GitRemoteService service = new(new GitTagService(), new GitConfigService());
        GitRemote activeRemote = new("public", ".", ".");

        SynchronizationSnapshot snapshot =
            await service.GetLocalConfiguredSynchronizationSnapshotAsync(
                repository.Repository,
                activeRemote,
                []);

        BranchSynchronizationItem branch = snapshot.CurrentBranch!;
        Assert.AreEqual(1, branch.PushAheadCount);
        Assert.AreEqual(1, branch.PushBehindCount);
        Assert.IsTrue(branch.RequiresForcePush);
        Assert.IsTrue(branch.CanPush);
    }

    [TestMethod]
    public async Task GetLocalConfiguredSynchronizationSnapshotAsync_UsesActiveRemoteAsFallback()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("tracked.txt", "initial");
        await repository.CommitAllAsync();
        await repository.RunGitAsync("branch", "-M", "main");
        await repository.RunGitAsync("remote", "add", "backup", ".");
        await repository.RunGitAsync("update-ref", "refs/remotes/backup/main", "HEAD");
        await repository.RunGitAsync("config", "--local", "remote.pushDefault", "");

        GitRemoteService service = new(new GitTagService(), new GitConfigService());
        GitRemote activeRemote = new("backup", ".", ".");

        SynchronizationSnapshot snapshot =
            await service.GetLocalConfiguredSynchronizationSnapshotAsync(
                repository.Repository,
                activeRemote,
                []);

        BranchSynchronizationItem branch = snapshot.CurrentBranch!;
        Assert.IsFalse(branch.HasUpstream);
        Assert.AreEqual("backup/main", branch.RemoteTrackingBranch);
        Assert.AreEqual("backup", branch.ConfiguredPushRemoteName);
        Assert.AreEqual("backup/main", branch.PushTrackingBranch);
        Assert.IsFalse(branch.HasPushRemoteOverride);
        Assert.IsFalse(branch.NeedsSynchronization);
    }

    [TestMethod]
    public async Task GetLocalConfiguredSynchronizationSnapshotAsync_PreservesDifferentlyNamedMissingUpstream()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("tracked.txt", "initial");
        await repository.CommitAllAsync();
        await repository.RunGitAsync("branch", "-M", "main");
        await repository.RunGitAsync("remote", "add", "origin", ".");
        await repository.RunGitAsync("remote", "add", "public", ".");
        await repository.RunGitAsync("config", "--local", "branch.main.remote", "origin");
        await repository.RunGitAsync(
            "config",
            "--local",
            "branch.main.merge",
            "refs/heads/release");

        GitConfigService configService = new();
        await configService.SetBranchUpstreamAsync(repository.Repository, "main", "public");
        GitRemoteService service = new(new GitTagService(), configService);
        GitRemote activeRemote = new("origin", ".", ".");

        SynchronizationSnapshot snapshot =
            await service.GetLocalConfiguredSynchronizationSnapshotAsync(
                repository.Repository,
                activeRemote,
                []);

        BranchSynchronizationItem branch = snapshot.CurrentBranch!;
        Assert.IsTrue(branch.HasUpstream);
        Assert.AreEqual("public/release", branch.UpstreamBranch);
        Assert.AreEqual("public/release", branch.RemoteTrackingBranch);
        Assert.IsFalse(branch.IsPublishedToRemote);
    }

    [TestMethod]
    public async Task FetchSynchronizationRemotesAsync_FetchesActiveUpstreamAndPushRemotes()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("tracked.txt", "initial");
        await repository.CommitAllAsync();
        await repository.RunGitAsync("branch", "-M", "main");
        await repository.RunGitAsync("remote", "add", "backup", ".");
        await repository.RunGitAsync("remote", "add", "origin", ".");
        await repository.RunGitAsync("remote", "add", "public", ".");
        await repository.RunGitAsync("config", "--local", "branch.main.remote", "origin");
        await repository.RunGitAsync(
            "config",
            "--local",
            "branch.main.merge",
            "refs/heads/main");
        await repository.RunGitAsync("config", "--local", "remote.pushDefault", "public");

        GitRemoteService service = new(new GitTagService(), new GitConfigService());
        GitRemote activeRemote = new("backup", ".", ".");

        await service.FetchSynchronizationRemotesAsync(repository.Repository, activeRemote);

        string remoteReferences = await repository.RunGitAsync(
            "for-each-ref",
            "--format=%(refname:short)",
            "refs/remotes");
        string[] references = remoteReferences.Split(
            ['\r', '\n'],
            System.StringSplitOptions.RemoveEmptyEntries);
        CollectionAssert.Contains(references, "backup/main");
        CollectionAssert.Contains(references, "origin/main");
        CollectionAssert.Contains(references, "public/main");
    }

    [TestMethod]
    public async Task PushBranchAsync_DoesNotCreateUpstreamConfiguration()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("tracked.txt", "initial");
        await repository.CommitAllAsync();
        await repository.RunGitAsync("branch", "-M", "main");
        await repository.RunGitAsync("remote", "add", "origin", ".");

        GitRemoteService service = new(new GitTagService(), new GitConfigService());

        await service.PushBranchAsync(
            repository.Repository,
            "origin",
            "main",
            forceWithLease: false);

        string branchConfiguration = await repository.RunGitAsync("config", "--local", "--list");
        Assert.IsFalse(branchConfiguration.Contains(
            "branch.main.remote",
            System.StringComparison.Ordinal));
        Assert.IsFalse(branchConfiguration.Contains(
            "branch.main.merge",
            System.StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task PushAsync_AtomicBatch_DoesNotUpdateAnyReferenceWhenOneIsRejected()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("tracked.txt", "initial");
        await repository.CommitAllAsync();
        await repository.RunGitAsync("branch", "-M", "main");
        await repository.RunGitAsync("init", "--bare", ".git/test-remote.git");
        await repository.RunGitAsync("remote", "add", "origin", ".git/test-remote.git");
        await repository.RunGitAsync("push", "origin", "main");
        await repository.RunGitAsync("branch", "protected", "main");
        await repository.RunGitAsync("push", "origin", "protected");

        await repository.RunGitAsync("checkout", "protected");
        repository.WriteFile("remote-change.txt", "remote change");
        await repository.CommitAllAsync("remote change");
        string remoteProtectedTip = await repository.RunGitAsync("rev-parse", "HEAD");
        await repository.RunGitAsync("push", "origin", "protected");

        await repository.RunGitAsync("checkout", "main");
        await repository.RunGitAsync("branch", "-f", "protected", "main");
        await repository.RunGitAsync("checkout", "protected");
        repository.WriteFile("local-change.txt", "local change");
        await repository.CommitAllAsync("local change");

        await repository.RunGitAsync("checkout", "-b", "valid", "main");
        repository.WriteFile("valid-change.txt", "valid change");
        await repository.CommitAllAsync("valid change");

        GitRemoteService service = new(new GitTagService(), new GitConfigService());
        GitPushRequest request = new(
            "origin",
            [
                new GitPushReferenceUpdate(GitPushReferenceKind.Branch, "valid"),
                new GitPushReferenceUpdate(GitPushReferenceKind.Branch, "protected")
            ],
            GitPushMode.Atomic);

        await Assert.ThrowsExactlyAsync<GitRemoteOperationException>(
            () => service.PushAsync(repository.Repository, request));

        string validReference = await repository.RunGitAsync(
            "--git-dir=.git/test-remote.git",
            "for-each-ref",
            "--format=%(refname)",
            "refs/heads/valid");
        string protectedTip = await repository.RunGitAsync(
            "--git-dir=.git/test-remote.git",
            "rev-parse",
            "refs/heads/protected");
        Assert.AreEqual("", validReference);
        Assert.AreEqual(remoteProtectedTip, protectedTip);
    }

    [TestMethod]
    public async Task PushAsync_AtomicUnsupported_ClassifiesFailure()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("tracked.txt", "initial");
        await repository.CommitAllAsync();
        await repository.RunGitAsync("branch", "-M", "main");
        await repository.RunGitAsync("init", "--bare", ".git/test-remote.git");
        await repository.RunGitAsync(
            "--git-dir=.git/test-remote.git",
            "config",
            "receive.advertiseAtomic",
            "false");
        await repository.RunGitAsync("remote", "add", "origin", ".git/test-remote.git");

        GitRemoteService service = new(new GitTagService(), new GitConfigService());
        GitPushRequest request = new(
            "origin",
            [new GitPushReferenceUpdate(GitPushReferenceKind.Branch, "main")],
            GitPushMode.Atomic);

        GitRemoteOperationException exception =
            await Assert.ThrowsExactlyAsync<GitRemoteOperationException>(
                () => service.PushAsync(repository.Repository, request));

        Assert.AreEqual(GitRemoteOperationErrorKind.AtomicNotSupported, exception.Kind);
    }
}
