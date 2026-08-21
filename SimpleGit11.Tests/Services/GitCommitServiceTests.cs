using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitCommitServiceTests
{
    [TestMethod]
    public async Task CommitAsync_AllowEmpty_AddsArgumentBeforeMessage()
    {
        RecordingGitCommandRunner runner = new();
        GitCommitService service = new(runner);

        await service.CommitAsync(
            CreateRepository(),
            "Empty commit",
            new GitCommitOptions(AllowEmpty: true));

        CollectionAssert.AreEqual(
            new[] { "commit", "--allow-empty", "-m", "Empty commit" },
            runner.Arguments.ToArray());
    }

    [TestMethod]
    public async Task AmendAsync_AllowEmptyWithoutMessage_PreservesMessage()
    {
        RecordingGitCommandRunner runner = new();
        GitCommitService service = new(runner);

        await service.AmendAsync(
            CreateRepository(),
            null,
            new GitCommitOptions(AllowEmpty: true));

        CollectionAssert.AreEqual(
            new[] { "commit", "--amend", "--allow-empty", "--no-edit" },
            runner.Arguments.ToArray());
    }

    [TestMethod]
    public async Task WouldCreateEmptyCommitAsync_UnbornBranchWithoutStagedChanges_ReturnsTrue()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        GitCommitService service = new();

        bool result = await service.WouldCreateEmptyCommitAsync(
            repository.Repository,
            amend: false);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task WouldCreateEmptyCommitAsync_StagedChanges_ReturnsFalse()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("file.txt", "content");
        await repository.RunGitAsync("add", "file.txt");
        GitCommitService service = new();

        bool result = await service.WouldCreateEmptyCommitAsync(
            repository.Repository,
            amend: false);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task WouldCreateEmptyCommitAsync_CleanExistingBranch_ReturnsTrue()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("file.txt", "content");
        await repository.CommitAllAsync("base");
        GitCommitService service = new();

        bool result = await service.WouldCreateEmptyCommitAsync(
            repository.Repository,
            amend: false);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task WouldCreateEmptyCommitAsync_ExistingEmptyCommitAmend_ReturnsTrue()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        await repository.RunGitAsync("commit", "--allow-empty", "-m", "base");
        await repository.RunGitAsync("commit", "--allow-empty", "-m", "empty");
        GitCommitService service = new();

        bool result = await service.WouldCreateEmptyCommitAsync(
            repository.Repository,
            amend: true);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task WouldCreateEmptyCommitAsync_EmptyRootCommitAmend_ReturnsTrue()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        await repository.RunGitAsync("commit", "--allow-empty", "-m", "empty root");
        GitCommitService service = new();

        bool result = await service.WouldCreateEmptyCommitAsync(
            repository.Repository,
            amend: true);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task WouldCreateEmptyCommitAsync_NonEmptyCommitAmend_ReturnsFalse()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        await repository.RunGitAsync("commit", "--allow-empty", "-m", "base");
        repository.WriteFile("file.txt", "content");
        await repository.CommitAllAsync("non-empty");
        GitCommitService service = new();

        bool result = await service.WouldCreateEmptyCommitAsync(
            repository.Repository,
            amend: true);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task WouldCreateEmptyCommitAsync_StagedTreeCancelsCommitChanges_ReturnsTrue()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        await repository.RunGitAsync("commit", "--allow-empty", "-m", "base");
        repository.WriteFile("file.txt", "content");
        await repository.CommitAllAsync("non-empty");
        await repository.RunGitAsync("read-tree", "HEAD^");
        GitCommitService service = new();

        bool result = await service.WouldCreateEmptyCommitAsync(
            repository.Repository,
            amend: true);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task CommitAsync_AllowEmpty_CreatesCommitInCleanRepository()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        await repository.RunGitAsync("commit", "--allow-empty", "-m", "base");
        GitCommitService service = new();

        await service.CommitAsync(
            repository.Repository,
            "empty",
            new GitCommitOptions(AllowEmpty: true));

        Assert.AreEqual("2", await repository.RunGitAsync("rev-list", "--count", "HEAD"));
    }

    [TestMethod]
    public async Task AmendAsync_AllowEmpty_RewritesExistingEmptyCommit()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        await repository.RunGitAsync("commit", "--allow-empty", "-m", "base");
        await repository.RunGitAsync("commit", "--allow-empty", "-m", "empty");
        string headBefore = await repository.RunGitAsync("rev-parse", "HEAD");
        GitCommitService service = new();

        await service.AmendAsync(
            repository.Repository,
            "amended empty",
            new GitCommitOptions(AllowEmpty: true));

        Assert.AreNotEqual(headBefore, await repository.RunGitAsync("rev-parse", "HEAD"));
        Assert.AreEqual("amended empty", await repository.RunGitAsync("log", "-1", "--format=%s"));
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
