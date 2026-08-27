using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitBranchServiceTests
{
    [TestMethod]
    public async Task CheckoutCommitAsync_SwitchesToDetachedHeadAtCommit()
    {
        RecordingGitCommandRunner runner = new();
        GitBranchService service = new(runner);

        await service.CheckoutCommitAsync(CreateRepository(), "0123456789abcdef");

        CollectionAssert.AreEqual(
            new[] { "switch", "--detach", "--", "0123456789abcdef" },
            runner.Arguments.ToArray());
    }

    [TestMethod]
    public async Task MergeAsync_Default_AddsNoFastForwardArgument()
    {
        RecordingGitCommandRunner runner = new();
        GitBranchService service = new(runner);

        await service.MergeAsync(
            CreateRepository(),
            CreateBranch("feature"),
            new GitBranchMergeOptions());

        CollectionAssert.AreEqual(
            new[] { "merge", "--no-ff", "feature" },
            runner.Arguments.ToArray());
    }

    [TestMethod]
    public async Task MergeAsync_UnrelatedHistories_DisablesAutomaticCommit()
    {
        RecordingGitCommandRunner runner = new();
        GitBranchService service = new(runner);

        await service.MergeAsync(
            CreateRepository(),
            CreateBranch("feature"),
            new GitBranchMergeOptions(AllowUnrelatedHistories: true));

        CollectionAssert.AreEqual(
            new[]
            {
                "merge",
                "--no-ff",
                "--allow-unrelated-histories",
                "--no-commit",
                "feature"
            },
            runner.Arguments.ToArray());
    }

    [TestMethod]
    public async Task MergeAsync_SquashUnrelatedHistories_PreservesSquashAndDisablesCommit()
    {
        RecordingGitCommandRunner runner = new();
        GitBranchService service = new(runner);

        await service.MergeAsync(
            CreateRepository(),
            CreateBranch("feature"),
            new GitBranchMergeOptions(Squash: true, AllowUnrelatedHistories: true));

        CollectionAssert.AreEqual(
            new[]
            {
                "merge",
                "--squash",
                "--allow-unrelated-histories",
                "--no-commit",
                "feature"
            },
            runner.Arguments.ToArray());
    }

    [TestMethod]
    public async Task PrepareSnapshotAsync_ResetsIndexAndWorkingTreeFromSourceBranch()
    {
        RecordingGitCommandRunner runner = new();
        GitBranchService service = new(runner);

        await service.PrepareSnapshotAsync(
            CreateRepository(),
            CreateBranch("dev"));

        CollectionAssert.AreEqual(
            new[] { "read-tree", "--reset", "-u", "dev" },
            runner.Arguments.ToArray());
    }

    [TestMethod]
    public async Task PrepareSnapshotAsync_ProducesExactSourceTreeWithoutMovingHead()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("shared.txt", "base");
        repository.WriteFile("obsolete.txt", "remove from snapshot");
        await repository.CommitAllAsync("base");
        await repository.RunGitAsync("switch", "-c", "dev");
        repository.WriteFile("shared.txt", "dev");
        repository.WriteFile("dev-only.txt", "dev");
        await repository.RunGitAsync("rm", "obsolete.txt");
        await repository.CommitAllAsync("dev snapshot");
        string sourceTree = await repository.RunGitAsync("rev-parse", "dev^{tree}");
        await repository.RunGitAsync("switch", "main");
        repository.WriteFile("main-only.txt", "main");
        await repository.CommitAllAsync("main state");
        string headBefore = await repository.RunGitAsync("rev-parse", "HEAD");
        GitBranchService service = new();

        await service.PrepareSnapshotAsync(repository.Repository, CreateBranch("dev"));

        Assert.AreEqual(headBefore, await repository.RunGitAsync("rev-parse", "HEAD"));
        Assert.AreEqual(sourceTree, await repository.RunGitAsync("write-tree"));
        Assert.AreEqual("dev", repository.ReadFile("shared.txt"));
        Assert.IsTrue(repository.FileExists("dev-only.txt"));
        Assert.IsFalse(repository.FileExists("obsolete.txt"));
        Assert.IsFalse(repository.FileExists("main-only.txt"));
    }

    [TestMethod]
    public void IsUnrelatedHistories_MatchingFatalMessage_ReturnsTrue()
    {
        GitCommandException exception = new(
            "fatal: refusing to merge unrelated histories",
            128);

        Assert.IsTrue(GitMergeFailureDetector.IsUnrelatedHistories(exception));
    }

    [TestMethod]
    public void IsUnrelatedHistories_OtherMergeFailure_ReturnsFalse()
    {
        GitCommandException exception = new("Automatic merge failed", 1);

        Assert.IsFalse(GitMergeFailureDetector.IsUnrelatedHistories(exception));
    }

    [TestMethod]
    public async Task RebaseAsync_ReplaysCurrentBranchOntoSelectedBranch()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("common.txt", "common");
        await repository.CommitAllAsync("common");
        await repository.RunGitAsync("branch", "feature");
        repository.WriteFile("main.txt", "main");
        await repository.CommitAllAsync("main");
        string mainHead = await repository.RunGitAsync("rev-parse", "HEAD");
        await repository.RunGitAsync("switch", "feature");
        repository.WriteFile("feature.txt", "feature");
        await repository.CommitAllAsync("feature");
        string featureHeadBefore = await repository.RunGitAsync("rev-parse", "HEAD");
        GitBranch selectedBranch = CreateBranch("main");
        GitBranchService service = new();

        GitBranchRebaseResult result = await service.RebaseAsync(
            repository.Repository,
            selectedBranch);

        string featureHeadAfter = await repository.RunGitAsync("rev-parse", "HEAD");
        string featureParent = await repository.RunGitAsync("rev-parse", "HEAD^");
        Assert.IsTrue(result.HeadChanged);
        Assert.AreNotEqual(featureHeadBefore, featureHeadAfter);
        Assert.AreEqual(mainHead, featureParent);
        Assert.AreEqual("feature", await repository.RunGitAsync("branch", "--show-current"));
    }

    [TestMethod]
    public async Task RebaseAsync_AlreadyBasedOnSelectedBranch_ReportsUnchangedHead()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        await repository.RunGitAsync("commit", "--allow-empty", "-m", "initial");
        await repository.RunGitAsync("branch", "base-branch");
        GitBranchService service = new();

        GitBranchRebaseResult result = await service.RebaseAsync(
            repository.Repository,
            CreateBranch("base-branch"));

        Assert.IsFalse(result.HeadChanged);
    }

    [TestMethod]
    public async Task GetLocalBranchesAsync_DetachedHead_ReturnsOnlyBranchReferences()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        await repository.RunGitAsync("commit", "--allow-empty", "-m", "initial");
        GitBranchService service = new();

        IReadOnlyList<GitBranch> attachedBranches =
            await service.GetLocalBranchesAsync(repository.Repository);

        Assert.HasCount(1, attachedBranches);
        Assert.AreEqual("main", attachedBranches[0].Name);
        Assert.IsTrue(attachedBranches[0].IsCurrent);

        string commitHash = await repository.RunGitAsync("rev-parse", "HEAD");
        await repository.RunGitAsync("switch", "--detach", commitHash);

        IReadOnlyList<GitBranch> branches =
            await service.GetLocalBranchesAsync(repository.Repository);

        Assert.HasCount(1, branches);
        Assert.AreEqual("main", branches[0].Name);
        Assert.IsFalse(branches[0].IsCurrent);
    }

    [TestMethod]
    public async Task GetRemoteBranchesAsync_ExcludesRemoteHeadSymbolicReference()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        await repository.RunGitAsync("commit", "--allow-empty", "-m", "initial");
        await repository.RunGitAsync("update-ref", "refs/remotes/origin/main", "HEAD");
        await repository.RunGitAsync(
            "symbolic-ref",
            "refs/remotes/origin/HEAD",
            "refs/remotes/origin/main");

        GitBranchService service = new();

        IReadOnlyList<GitBranch> branches =
            await service.GetRemoteBranchesAsync(repository.Repository);

        Assert.HasCount(1, branches);
        Assert.AreEqual("origin/main", branches[0].Name);
    }

    private static GitBranch CreateBranch(string name)
    {
        return new GitBranch(name, false, false, "", "", null);
    }

    private static RepositoryInfo CreateRepository() =>
        new("C:\\repository", "repository", "main");

    private sealed class RecordingGitCommandRunner : IGitCommandRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Arguments = arguments.ToArray();
            return Task.FromResult(new GitCommandResult(0, "", ""));
        }
    }
}
