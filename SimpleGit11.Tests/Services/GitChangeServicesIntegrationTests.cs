using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public sealed class GitChangeServicesIntegrationTests
{
    [TestMethod]
    public async Task AddAsync_MissingGitIgnore_CreatesFileWithRootedRule()
    {
        await using TemporaryGitRepository repository =
            await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("draft.txt", "draft");
        GitIgnoreService service = new();
        GitChangedFile changedFile = new(
            "draft.txt",
            "Untracked",
            state: GitChangeState.Unstaged);

        await service.AddAsync(repository.Repository, changedFile);

        Assert.AreEqual(
            $"/draft.txt{Environment.NewLine}",
            repository.ReadFile(".gitignore"));
    }

    [TestMethod]
    public async Task AddAsync_UntrackedFile_AppendsLiteralRuleAndHidesFileFromStatus()
    {
        await using TemporaryGitRepository repository =
            await TemporaryGitRepository.CreateAsync();
        repository.WriteFile(".gitignore", "*.tmp");
        repository.WriteFile("notes/[draft].txt", "draft");
        GitIgnoreService service = new();
        GitChangedFile changedFile = new(
            "notes/[draft].txt",
            "Untracked",
            state: GitChangeState.Unstaged);

        await service.AddAsync(repository.Repository, changedFile);

        string expectedRule = $"*.tmp{Environment.NewLine}/notes/\\[draft\\].txt{Environment.NewLine}";
        Assert.AreEqual(expectedRule, repository.ReadFile(".gitignore"));

        GitStatusSnapshot status = await new GitStatusService().GetStatusAsync(repository.Repository);
        Assert.IsFalse(status.UnstagedChanges.Any(change => change.Path == changedFile.Path));
    }

    [TestMethod]
    public async Task DiscardFileAsync_UntrackedPathBeginningWithDash_RemovesOnlySelectedFile()
    {
        await using TemporaryGitRepository repository =
            await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("-selected file.txt", "selected");
        repository.WriteFile("keep.txt", "keep");
        GitChangeRecoveryService service = new();
        GitChangedFile changedFile = new(
            "-selected file.txt",
            "Untracked",
            state: GitChangeState.Unstaged);

        await service.DiscardFileAsync(repository.Repository, changedFile);

        Assert.IsFalse(repository.FileExists("-selected file.txt"));
        Assert.IsTrue(repository.FileExists("keep.txt"));
    }

    [TestMethod]
    public async Task DiscardFileAsync_ModifiedTrackedFile_RestoresOnlySelectedFile()
    {
        await using TemporaryGitRepository repository =
            await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("selected.txt", "original selected");
        repository.WriteFile("keep.txt", "original keep");
        await repository.CommitAllAsync();
        repository.WriteFile("selected.txt", "modified selected");
        repository.WriteFile("keep.txt", "modified keep");
        GitChangeRecoveryService service = new();
        GitChangedFile changedFile = new(
            "selected.txt",
            "Modified",
            state: GitChangeState.Unstaged);

        await service.DiscardFileAsync(repository.Repository, changedFile);

        Assert.AreEqual("original selected", repository.ReadFile("selected.txt"));
        Assert.AreEqual("modified keep", repository.ReadFile("keep.txt"));
    }

    [TestMethod]
    public async Task DiscardUnstagedChangesAsync_RestoresTrackedAndRemovesUntrackedFiles()
    {
        await using TemporaryGitRepository repository =
            await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("tracked.txt", "original");
        await repository.CommitAllAsync();
        repository.WriteFile("tracked.txt", "modified");
        repository.WriteFile("nested/untracked.txt", "untracked");
        GitChangeRecoveryService service = new();

        await service.DiscardUnstagedChangesAsync(repository.Repository);

        Assert.AreEqual("original", repository.ReadFile("tracked.txt"));
        Assert.IsFalse(repository.FileExists("nested/untracked.txt"));
    }

    [TestMethod]
    public async Task ClearStashesAsync_MultipleStashes_RemovesAllStashes()
    {
        await using TemporaryGitRepository repository =
            await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("tracked.txt", "initial");
        await repository.CommitAllAsync();
        GitStashService service = new();

        repository.WriteFile("tracked.txt", "first change");
        await service.CreateStashAsync(repository.Repository);
        repository.WriteFile("tracked.txt", "second change");
        await service.CreateStashAsync(repository.Repository);

        Assert.HasCount(2, await service.GetStashesAsync(repository.Repository));

        await service.ClearStashesAsync(repository.Repository);

        Assert.IsEmpty(await service.GetStashesAsync(repository.Repository));
    }
}
