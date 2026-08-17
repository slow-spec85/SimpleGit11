using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitCommitCherryPickTests
{
    [TestMethod]
    public async Task CherryPickAsync_MultipleCommits_RunsSingleCommandInProvidedOrder()
    {
        RecordingGitCommandRunner runner = new();
        GitCommitService service = new(runner);

        await service.CherryPickAsync(
            CreateRepository(),
            [CreateCommit("oldest"), CreateCommit("newest")],
            GitCherryPickOptions.Default);

        CollectionAssert.AreEqual(
            new[] { "cherry-pick", "--no-edit", "oldest", "newest" },
            runner.Arguments.ToArray());
    }

    [TestMethod]
    [DataRow(true, false, false, "-x")]
    [DataRow(false, true, false, "--signoff")]
    [DataRow(false, false, true, "--no-commit")]
    public async Task CherryPickAsync_Option_AddsMatchingArgument(
        bool appendSourceReference,
        bool addSignOff,
        bool noCommit,
        string expectedArgument)
    {
        RecordingGitCommandRunner runner = new();
        GitCommitService service = new(runner);
        GitCherryPickOptions options = new(
            AppendSourceReference: appendSourceReference,
            AddSignOff: addSignOff,
            NoCommit: noCommit);

        await service.CherryPickAsync(CreateRepository(), [CreateCommit("commit")], options);

        CollectionAssert.Contains(runner.Arguments.ToArray(), expectedArgument);
    }

    [TestMethod]
    public async Task CherryPickAsync_MergeCommit_AddsSelectedMainlineParent()
    {
        RecordingGitCommandRunner runner = new();
        GitCommitService service = new(runner);
        GitCommit mergeCommit = CreateCommit("merge", "parent-1", "parent-2");

        await service.CherryPickAsync(
            CreateRepository(),
            [mergeCommit],
            new GitCherryPickOptions(MainlineParentNumber: 2));

        CollectionAssert.AreEqual(
            new[] { "cherry-pick", "--no-edit", "--mainline", "2", "merge" },
            runner.Arguments.ToArray());
    }

    [TestMethod]
    public async Task CherryPickAsync_MergeCommitWithOtherCommits_ThrowsArgumentException()
    {
        GitCommitService service = new(new RecordingGitCommandRunner());

        await Assert.ThrowsAsync<ArgumentException>(() => service.CherryPickAsync(
            CreateRepository(),
            [CreateCommit("ordinary"), CreateCommit("merge", "parent-1", "parent-2")],
            new GitCherryPickOptions(MainlineParentNumber: 1)));
    }

    [TestMethod]
    public async Task CherryPickAsync_MultipleCommits_AppliesOldestThenNewest()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("base.txt", "base");
        await repository.CommitAllAsync("base");
        await repository.RunGitAsync("switch", "-c", "source");
        repository.WriteFile("oldest.txt", "oldest");
        await repository.CommitAllAsync("oldest");
        string oldestHash = await repository.RunGitAsync("rev-parse", "HEAD");
        repository.WriteFile("newest.txt", "newest");
        await repository.CommitAllAsync("newest");
        string newestHash = await repository.RunGitAsync("rev-parse", "HEAD");
        await repository.RunGitAsync("switch", "main");
        GitCommitService service = new();

        await service.CherryPickAsync(
            repository.Repository,
            [CreateCommit(oldestHash), CreateCommit(newestHash)],
            GitCherryPickOptions.Default);

        string[] subjects = (await repository.RunGitAsync("log", "-2", "--format=%s"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        CollectionAssert.AreEqual(new[] { "newest", "oldest" }, subjects);
        Assert.IsTrue(repository.FileExists("oldest.txt"));
        Assert.IsTrue(repository.FileExists("newest.txt"));
    }

    [TestMethod]
    public async Task CherryPickAsync_MergeCommit_AppliesChangesRelativeToSelectedParent()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("base.txt", "base");
        await repository.CommitAllAsync("base");
        await repository.RunGitAsync("branch", "target");
        await repository.RunGitAsync("switch", "-c", "topic");
        repository.WriteFile("topic.txt", "topic");
        await repository.CommitAllAsync("topic");
        await repository.RunGitAsync("switch", "main");
        repository.WriteFile("main.txt", "main");
        await repository.CommitAllAsync("main");
        await repository.RunGitAsync("merge", "--no-ff", "topic", "-m", "merge topic");
        string mergeHash = await repository.RunGitAsync("rev-parse", "HEAD");
        await repository.RunGitAsync("switch", "target");
        GitCommit mergeCommit = CreateCommit(
            mergeHash,
            await repository.RunGitAsync("rev-parse", $"{mergeHash}^1"),
            await repository.RunGitAsync("rev-parse", $"{mergeHash}^2"));
        GitCommitService service = new();

        await service.CherryPickAsync(
            repository.Repository,
            [mergeCommit],
            new GitCherryPickOptions(MainlineParentNumber: 1));

        Assert.IsTrue(repository.FileExists("topic.txt"));
        Assert.IsFalse(repository.FileExists("main.txt"));
        Assert.AreEqual("merge topic", await repository.RunGitAsync("log", "-1", "--format=%s"));
    }

    private static RepositoryInfo CreateRepository() => new("C:\\repository", "repository", "main");

    private static GitCommit CreateCommit(string hash, params string[] parentHashes) => new(
        hash,
        hash,
        "Author",
        "author@example.invalid",
        null,
        hash,
        hash,
        parentHashes: parentHashes);

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
