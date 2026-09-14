using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitWorktreeServiceTests
{
    [TestMethod]
    public async Task GetWorktreesAsync_OldGitWithoutNullFormat_FallsBackToLineFormat()
    {
        OldGitCommandRunner runner = new();
        GitWorktreeService service = new(runner);
        RepositoryInfo repository = new(
            Environment.CurrentDirectory,
            "repository",
            "main",
            mainWorktreePath: Environment.CurrentDirectory);

        GitWorktree worktree = (await service.GetWorktreesAsync(repository)).Single();

        Assert.AreEqual("main", worktree.BranchName);
        CollectionAssert.AreEqual(
            new[]
            {
                "worktree list --porcelain -z",
                "worktree list --porcelain"
            },
            runner.Commands);
    }

    [TestMethod]
    public async Task CleanWorktree_RemovesNormallyAndKeepsBranch()
    {
        await using TemporaryGitRepository repository = await CreateRepositoryAsync();
        using TemporaryDirectory directory = new();
        GitWorktreeService service = new();
        GitWorktree worktree = await AddWorktreeAsync(repository, directory, service);

        WorktreeRemovalState state = await service.GetRemovalStateAsync(repository.Repository, worktree);
        Assert.IsTrue(state.CanRemove);
        Assert.IsFalse(state.RequiresForce);
        await service.RemoveAsync(repository.Repository, worktree, false);

        Assert.IsFalse(Directory.Exists(worktree.Path));
        Assert.AreEqual(await repository.RunGitAsync("rev-parse", "main"),
            await repository.RunGitAsync("rev-parse", "--verify", "refs/heads/topic"));
    }

    [TestMethod]
    [DataRow("modified")]
    [DataRow("staged")]
    [DataRow("untracked")]
    [DataRow("deleted")]
    [DataRow("conflict")]
    public async Task DirtyWorktree_RequiresForceDespiteStatusConfiguration(string change)
    {
        await using TemporaryGitRepository repository = await CreateRepositoryAsync();
        using TemporaryDirectory directory = new();
        GitWorktreeService service = new();
        GitWorktree worktree = await AddWorktreeAsync(repository, directory, service);
        await repository.RunGitAsync("config", "status.showUntrackedFiles", "no");
        if (change == "deleted")
        {
            File.Delete(Path.Combine(worktree.Path, "tracked.txt"));
        }
        else
        {
            File.WriteAllText(Path.Combine(worktree.Path, change == "untracked" ? "new.txt" : "tracked.txt"), "changed");
        }
        if (change == "staged")
        {
            await repository.RunGitAsync("-C", worktree.Path, "add", "tracked.txt");
        }
        if (change == "conflict")
        {
            await repository.RunGitAsync("-C", worktree.Path, "commit", "-am", "topic change");
            repository.WriteFile("tracked.txt", "main change");
            await repository.CommitAllAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RunGitAsync("-C", worktree.Path, "merge", "main"));
        }

        WorktreeRemovalState state = await service.GetRemovalStateAsync(repository.Repository, worktree);
        Assert.IsTrue(state.HasChanges);
        Assert.IsTrue(state.RequiresForce);
        Assert.IsFalse(state.HasSubmodules);
        await repository.RunGitAsync("config", "status.showUntrackedFiles", "all");
        await Assert.ThrowsAsync<GitCommandException>(() => service.RemoveAsync(repository.Repository, worktree, false));
        Assert.IsTrue(Directory.Exists(worktree.Path));
        await service.RemoveAsync(repository.Repository, worktree, true);
        Assert.IsFalse(Directory.Exists(worktree.Path));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Submodules_OnlyInitializedOrRetainedDataRequiresForce(bool deinitialize)
    {
        await using TemporaryGitRepository submodule = await CreateRepositoryAsync();
        await using TemporaryGitRepository repository = await CreateRepositoryAsync();
        using TemporaryDirectory directory = new();
        await repository.RunGitAsync("-c", "protocol.file.allow=always", "submodule", "add", submodule.Repository.Path, "module with spaces");
        await repository.CommitAllAsync();
        GitWorktreeService service = new();
        GitWorktree worktree = await AddWorktreeAsync(repository, directory, service);

        WorktreeRemovalState uninitialized = await service.GetRemovalStateAsync(repository.Repository, worktree);
        Assert.IsFalse(uninitialized.RequiresForce);
        await repository.RunGitAsync("-C", worktree.Path, "-c", "protocol.file.allow=always", "submodule", "update", "--init");
        if (deinitialize)
        {
            await repository.RunGitAsync("-C", worktree.Path, "submodule", "deinit", "--all");
        }

        WorktreeRemovalState state = await service.GetRemovalStateAsync(repository.Repository, worktree);
        Assert.IsTrue(state.HasSubmodules);
        Assert.IsFalse(state.HasChanges);
        Assert.IsTrue(state.RequiresForce);
        await Assert.ThrowsAsync<GitCommandException>(() => service.RemoveAsync(repository.Repository, worktree, false));
        await service.RemoveAsync(repository.Repository, worktree, true);
        Assert.IsFalse(Directory.Exists(worktree.Path));
        Assert.IsTrue(submodule.FileExists("tracked.txt"));
    }

    [TestMethod]
    public async Task MainWorktree_IsBlockedEvenWithStaleFlagsAndForce()
    {
        await using TemporaryGitRepository repository = await CreateRepositoryAsync();
        GitWorktreeService service = new();
        GitWorktree worktree = (await service.GetWorktreesAsync(repository.Repository)).Single() with { IsMain = false };

        WorktreeRemovalState state = await service.GetRemovalStateAsync(repository.Repository, worktree);
        Assert.AreEqual(WorktreeRemovalBlocker.MainOrBare, state.Blocker);
        Assert.IsFalse(state.RequiresForce);
        await Assert.ThrowsAsync<GitCommandException>(() => service.RemoveAsync(repository.Repository, worktree, true));
        Assert.IsTrue(repository.FileExists("tracked.txt"));
    }

    [TestMethod]
    public async Task LockAfterPreflight_IsNotOverriddenByForce()
    {
        await using TemporaryGitRepository repository = await CreateRepositoryAsync();
        using TemporaryDirectory directory = new();
        GitWorktreeService service = new();
        GitWorktree worktree = await AddWorktreeAsync(repository, directory, service);
        Assert.IsTrue((await service.GetRemovalStateAsync(repository.Repository, worktree)).CanRemove);
        await repository.RunGitAsync("worktree", "lock", "--reason", "external lock", worktree.Path);

        WorktreeRemovalState state = await service.GetRemovalStateAsync(repository.Repository, worktree);
        Assert.AreEqual(WorktreeRemovalBlocker.Locked, state.Blocker);
        Assert.IsFalse(state.RequiresForce);
        await Assert.ThrowsAsync<GitCommandException>(() => service.RemoveAsync(repository.Repository, worktree, true));
        Assert.IsTrue(Directory.Exists(worktree.Path));
    }

    [TestMethod]
    public async Task RemovedWorktree_IsUnavailable()
    {
        await using TemporaryGitRepository repository = await CreateRepositoryAsync();
        using TemporaryDirectory directory = new();
        GitWorktreeService service = new();
        GitWorktree worktree = await AddWorktreeAsync(repository, directory, service);
        await repository.RunGitAsync("worktree", "remove", worktree.Path);

        WorktreeRemovalState state = await service.GetRemovalStateAsync(repository.Repository, worktree);
        Assert.AreEqual(WorktreeRemovalBlocker.Unavailable, state.Blocker);
        Assert.IsFalse(state.RequiresForce);
    }

    [TestMethod]
    public async Task InvalidGitFile_FailsPreflightWithoutOfferingForce()
    {
        await using TemporaryGitRepository repository = await CreateRepositoryAsync();
        using TemporaryDirectory directory = new();
        GitWorktreeService service = new();
        GitWorktree worktree = await AddWorktreeAsync(repository, directory, service);
        string gitFile = Path.Combine(worktree.Path, ".git");
        File.SetAttributes(gitFile, FileAttributes.Normal);
        File.WriteAllText(gitFile, "invalid");

        await Assert.ThrowsAsync<GitCommandException>(() => service.GetRemovalStateAsync(repository.Repository, worktree));
        Assert.IsTrue(File.Exists(Path.Combine(worktree.Path, "tracked.txt")));
    }

    [TestMethod]
    public async Task MissingGitFile_IsUnavailableInsteadOfInspectingParentRepository()
    {
        await using TemporaryGitRepository repository = await CreateRepositoryAsync();
        using TemporaryDirectory directory = new();
        GitWorktreeService service = new();
        GitWorktree worktree = await AddWorktreeAsync(repository, directory, service);
        File.Delete(Path.Combine(worktree.Path, ".git"));

        WorktreeRemovalState state = await service.GetRemovalStateAsync(repository.Repository, worktree);
        Assert.AreEqual(WorktreeRemovalBlocker.Unavailable, state.Blocker);
        Assert.IsFalse(state.RequiresForce);
    }

    private static async Task<TemporaryGitRepository> CreateRepositoryAsync()
    {
        TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("tracked.txt", "base");
        await repository.CommitAllAsync();
        return repository;
    }

    private static async Task<GitWorktree> AddWorktreeAsync(
        TemporaryGitRepository repository, TemporaryDirectory directory, GitWorktreeService service)
    {
        string path = directory.GetPath("linked worktree");
        await repository.RunGitAsync("worktree", "add", "-b", "topic", path);
        return (await service.GetWorktreesAsync(repository.Repository)).Single(worktree => !worktree.IsMain);
    }

    private sealed class OldGitCommandRunner : IGitCommandRunner
    {
        public List<string> Commands { get; } = [];

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            string command = string.Join(' ', arguments);
            Commands.Add(command);
            if (arguments.Contains("-z"))
            {
                throw new GitCommandException("error: unknown switch `z'", 129);
            }

            string output = $"worktree {workingDirectory}\n"
                + "HEAD 1111111111111111111111111111111111111111\n"
                + "branch refs/heads/main\n\n";
            return Task.FromResult(new GitCommandResult(0, output, ""));
        }
    }
}
