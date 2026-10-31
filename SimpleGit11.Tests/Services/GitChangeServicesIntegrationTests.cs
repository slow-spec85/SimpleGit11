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
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task DiscardFileAsync_UnstagedEntryPreservesStagedContent(bool newlyAdded, bool deleted)
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("keep.txt", "original keep");
        if (!newlyAdded) { repository.WriteFile("selected.txt", "committed content"); }
        await repository.CommitAllAsync();
        string selectedPath = repository.WriteFile("selected.txt", "staged content");
        await repository.RunGitAsync("add", "--", "selected.txt");
        string indexBefore = await repository.RunGitAsync("write-tree");
        if (deleted) { System.IO.File.Delete(selectedPath); }
        else { repository.WriteFile("selected.txt", "unstaged content"); }
        repository.WriteFile("keep.txt", "unrelated edits");
        GitStatusService statusService = new();
        GitStatusSnapshot before = await statusService.GetStatusAsync(repository.Repository);
        Assert.IsTrue(before.StagedChanges.Any(file => file.Path == "selected.txt"));
        GitChangedFile selected = before.UnstagedChanges.Single(file => file.Path == "selected.txt");

        await new GitChangeRecoveryService().DiscardFileAsync(repository.Repository, selected);

        Assert.AreEqual("staged content", repository.ReadFile("selected.txt"));
        Assert.AreEqual(indexBefore, await repository.RunGitAsync("write-tree"));
        Assert.AreEqual("unrelated edits", repository.ReadFile("keep.txt"));
        GitStatusSnapshot after = await statusService.GetStatusAsync(repository.Repository);
        Assert.IsTrue(after.StagedChanges.Any(file => file.Path == "selected.txt"));
        Assert.IsFalse(after.UnstagedChanges.Any(file => file.Path == "selected.txt"));
    }

    [TestMethod]
    public async Task DiscardFilesAsync_UnstagedSelectionPreservesTheEntireIndex()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("first.txt", "original first");
        repository.WriteFile("second.txt", "original second");
        await repository.CommitAllAsync();
        repository.WriteFile("first.txt", "staged first");
        repository.WriteFile("second.txt", "staged second");
        await repository.RunGitAsync("add", "--all");
        string indexBefore = await repository.RunGitAsync("write-tree");
        repository.WriteFile("first.txt", "unstaged first");
        repository.WriteFile("second.txt", "unstaged second");
        GitStatusSnapshot status = await new GitStatusService().GetStatusAsync(repository.Repository);

        await new GitChangeRecoveryService().DiscardFilesAsync(repository.Repository, status.UnstagedChanges);

        Assert.AreEqual(indexBefore, await repository.RunGitAsync("write-tree"));
        Assert.AreEqual("staged first", repository.ReadFile("first.txt"));
        Assert.AreEqual("staged second", repository.ReadFile("second.txt"));
        Assert.IsEmpty((await new GitStatusService().GetStatusAsync(repository.Repository)).UnstagedChanges);
    }

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

    [TestMethod]
    public async Task CreateStashAsync_SelectedPaths_StashesOnlyThosePathsIncludingUntrackedFiles()
    {
        await using TemporaryGitRepository repository =
            await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("selected.txt", "original");
        repository.WriteFile("keep.txt", "original");
        await repository.CommitAllAsync();

        repository.WriteFile("selected.txt", "staged");
        await repository.RunGitAsync("add", "selected.txt");
        repository.WriteFile("selected.txt", "unstaged");
        repository.WriteFile("keep.txt", "keep modified");
        repository.WriteFile("-selected [draft].txt", "new file");
        repository.WriteFile("other untracked.txt", "keep untracked");
        GitStashService service = new();

        await service.CreateStashAsync(
            repository.Repository,
            ["selected.txt", "-selected [draft].txt"]);

        Assert.AreEqual("original", repository.ReadFile("selected.txt"));
        Assert.IsFalse(repository.FileExists("-selected [draft].txt"));
        Assert.AreEqual("keep modified", repository.ReadFile("keep.txt"));
        Assert.IsTrue(repository.FileExists("other untracked.txt"));
        Assert.AreEqual(string.Empty, await repository.RunGitAsync("diff", "--cached", "--", "selected.txt"));
        IReadOnlyList<GitStash> stashes = await service.GetStashesAsync(repository.Repository);
        Assert.HasCount(1, stashes);

        await service.ApplyStashAsync(repository.Repository, stashes[0]);

        Assert.AreEqual("unstaged", repository.ReadFile("selected.txt"));
        Assert.IsTrue(repository.FileExists("-selected [draft].txt"));
        Assert.AreEqual("keep modified", repository.ReadFile("keep.txt"));
    }
}
