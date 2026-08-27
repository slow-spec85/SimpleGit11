using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitStatusServiceTests
{
    [TestMethod]
    public async Task GetStatusAsync_DirtySubmodule_MarksChangeAsSubmodule()
    {
        await using TemporaryGitRepository submodule = await TemporaryGitRepository.CreateAsync();
        submodule.WriteFile("library.txt", "first");
        await submodule.CommitAllAsync();
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        await repository.RunGitAsync(
            "-c",
            "protocol.file.allow=always",
            "submodule",
            "add",
            submodule.Repository.Path,
            "External/Library");
        await repository.CommitAllAsync();
        repository.WriteFile("External/Library/library.txt", "changed");
        GitStatusService service = new();

        GitStatusSnapshot snapshot = await service.GetStatusAsync(repository.Repository);

        Assert.HasCount(1, snapshot.UnstagedChanges);
        GitChangedFile change = snapshot.UnstagedChanges[0];
        Assert.AreEqual("External/Library", change.Path.Replace('\\', '/'));
        Assert.IsTrue(change.IsSubmodule);
        Assert.IsFalse(change.CanDiscard);
        Assert.IsFalse(change.CanShowFileContent);
    }

    [TestMethod]
    public async Task GetOperationStateAsync_MergeMarker_ReturnsMergeWithPreparedMessage()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        await repository.RunGitAsync("commit", "--allow-empty", "-m", "initial");
        repository.WriteFile(".git/MERGE_HEAD", new string('a', 40));
        repository.WriteFile(".git/MERGE_MSG", "Merge branch 'feature'\n");
        GitStatusService service = new();

        GitOperationState state = await service.GetOperationStateAsync(repository.Repository);

        Assert.AreEqual(GitOperationKind.Merge, state.Kind);
        Assert.AreEqual("Merge branch 'feature'", state.PreparedCommitMessage);
    }

    [TestMethod]
    public async Task GetOperationStateAsync_RebaseDirectory_ReturnsRebase()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        Directory.CreateDirectory(Path.Combine(repository.Repository.Path, ".git", "rebase-merge"));
        GitStatusService service = new();

        GitOperationState state = await service.GetOperationStateAsync(repository.Repository);

        Assert.AreEqual(GitOperationKind.Rebase, state.Kind);
    }

    [TestMethod]
    public async Task GetOperationStateAsync_ApplyMailboxDirectory_DoesNotReportRebase()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile(".git/rebase-apply/applying", "");
        GitStatusService service = new();

        GitOperationState state = await service.GetOperationStateAsync(repository.Repository);

        Assert.AreEqual(GitOperationKind.None, state.Kind);
    }

    [TestMethod]
    [DataRow("CHERRY_PICK_HEAD", GitOperationKind.CherryPick)]
    [DataRow("REVERT_HEAD", GitOperationKind.Revert)]
    public async Task GetOperationStateAsync_SequencerMarker_ReturnsOperationWithPreparedMessage(
        string markerName,
        GitOperationKind expectedKind)
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile($".git/{markerName}", new string('a', 40));
        repository.WriteFile(".git/MERGE_MSG", "Prepared message\n");
        GitStatusService service = new();

        GitOperationState state = await service.GetOperationStateAsync(repository.Repository);

        Assert.AreEqual(expectedKind, state.Kind);
        Assert.AreEqual("Prepared message", state.PreparedCommitMessage);
    }
}
