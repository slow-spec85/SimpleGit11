using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Presentation.Services;
using SimpleGit11.Services;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class GitCommitWorkflowServiceTests
{
    [TestMethod]
    public async Task CreateAsync_EmptyCommitConfirmed_UsesAllowEmpty()
    {
        TestGitCommitService commitService = new() { WouldCreateEmptyCommit = true };
        TestDialogService dialogService = new() { ConfirmationResult = true };
        GitCommitWorkflowService service = CreateService(commitService, dialogService);

        GitCommitOperationResult result = await service.CreateAsync(
            CreateRepository(),
            "empty");

        Assert.IsTrue(result.Completed);
        Assert.IsTrue(commitService.LastOptions?.AllowEmpty);
        Assert.AreEqual("EmptyCommitDialogTitle", dialogService.LastTitle);
    }

    [TestMethod]
    public async Task AmendAsync_EmptyCommitRejected_DoesNotRunAmend()
    {
        TestGitCommitService commitService = new() { WouldCreateEmptyCommit = true };
        TestDialogService dialogService = new() { ConfirmationResult = false };
        GitCommitWorkflowService service = CreateService(commitService, dialogService);

        GitCommitOperationResult result = await service.AmendAsync(
            CreateRepository(),
            "message");

        Assert.IsFalse(result.Completed);
        Assert.AreEqual(0, commitService.AmendCallCount);
        Assert.AreEqual("EmptyAmendDialogTitle", dialogService.LastTitle);
    }

    [TestMethod]
    public async Task CreateAsync_NonEmptyCommit_DoesNotShowConfirmation()
    {
        TestGitCommitService commitService = new() { WouldCreateEmptyCommit = false };
        TestDialogService dialogService = new();
        GitCommitWorkflowService service = CreateService(commitService, dialogService);

        GitCommitOperationResult result = await service.CreateAsync(
            CreateRepository(),
            "message");

        Assert.IsTrue(result.Completed);
        Assert.IsFalse(commitService.LastOptions?.AllowEmpty);
        Assert.AreEqual(0, dialogService.ConfirmationCount);
    }

    [TestMethod]
    public async Task CompleteMergeAsync_SkipsEmptyCommitCheck()
    {
        TestGitCommitService commitService = new() { WouldCreateEmptyCommit = true };
        TestDialogService dialogService = new();
        GitCommitWorkflowService service = CreateService(commitService, dialogService);

        GitCommitOperationResult result = await service.CompleteMergeAsync(
            CreateRepository(),
            "merge");

        Assert.IsTrue(result.Completed);
        Assert.AreEqual(0, commitService.EmptyCheckCount);
        Assert.AreEqual(0, dialogService.ConfirmationCount);
    }

    private static GitCommitWorkflowService CreateService(
        TestGitCommitService commitService,
        TestDialogService dialogService)
    {
        return new GitCommitWorkflowService(
            commitService,
            dialogService,
            new TestLocalizationService());
    }

    private static RepositoryInfo CreateRepository() =>
        new("C:\\repository", "repository", "main");

    private sealed class TestGitCommitService : IGitCommitService
    {
        public bool WouldCreateEmptyCommit { get; set; }

        public int EmptyCheckCount { get; private set; }

        public int AmendCallCount { get; private set; }

        public GitCommitOptions? LastOptions { get; private set; }

        public Task<string> CommitAsync(
            RepositoryInfo repository,
            string message,
            GitCommitOptions options)
        {
            LastOptions = options;
            return Task.FromResult("created");
        }

        public Task<string> AmendAsync(
            RepositoryInfo repository,
            string? message,
            GitCommitOptions options)
        {
            AmendCallCount++;
            LastOptions = options;
            return Task.FromResult("amended");
        }

        public Task<bool> WouldCreateEmptyCommitAsync(
            RepositoryInfo repository,
            bool amend)
        {
            EmptyCheckCount++;
            return Task.FromResult(WouldCreateEmptyCommit);
        }

        public Task CherryPickAsync(
            RepositoryInfo repository,
            IReadOnlyList<GitCommit> commits,
            GitCherryPickOptions options)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestDialogService : IDialogService
    {
        public bool ConfirmationResult { get; set; }

        public int ConfirmationCount { get; private set; }

        public string? LastTitle { get; private set; }

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string primaryButtonText)
        {
            ConfirmationCount++;
            LastTitle = title;
            return Task.FromResult(ConfirmationResult);
        }

        public Task<int?> ShowCherryPickMainlineDialogAsync(GitCommit commit) =>
            throw new NotSupportedException();

        public Task<bool> ConfirmCherryPickAsync(
            string branchName,
            IReadOnlyList<GitCommit> commits,
            GitCherryPickOptions options) => throw new NotSupportedException();

        public Task<string?> ShowTextInputAsync(TextInputDialogRequest request) =>
            throw new NotSupportedException();

        public Task<CommitDialogResult?> ShowCommitDialogAsync(CommitDialogRequest request) =>
            throw new NotSupportedException();

        public Task<BranchCreationRequest?> ShowCreateBranchDialogAsync(
            RepositoryInfo repository) => throw new NotSupportedException();

        public Task<string?> ShowRenameBranchDialogAsync(GitBranch branch) =>
            throw new NotSupportedException();

        public Task<string?> ShowBranchDescriptionDialogAsync(GitBranch branch) =>
            throw new NotSupportedException();

        public Task<TagCreationRequest?> ShowCreateTagDialogAsync(
            IReadOnlyList<GitCommit> commits) => throw new NotSupportedException();

        public Task<TagConflictResolution> ShowTagConflictDialogAsync(
            string tagName,
            string localHash,
            string remoteHash,
            string remoteName) => throw new NotSupportedException();

        public Task<WorktreeCreationRequest?> ShowCreateWorktreeDialogAsync(
            RepositoryInfo repository,
            string path,
            string startPoint,
            string newBranchName = "",
            WorktreeCreationMode creationMode = WorktreeCreationMode.ExistingBranch,
            bool canUseExistingBranch = true,
            GitRevisionKind startPointKind = GitRevisionKind.Branch) =>
            throw new NotSupportedException();

        public Task<GitArchiveDialogResult?> ShowArchiveDialogAsync(
            RepositoryInfo repository) => throw new NotSupportedException();
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public AppLanguage CurrentLanguage => AppLanguage.English;

        public string GetString(string resourceKey) => resourceKey;

        public void ApplyLanguage()
        {
        }

        public void SetLanguage(AppLanguage language)
        {
        }
    }
}
