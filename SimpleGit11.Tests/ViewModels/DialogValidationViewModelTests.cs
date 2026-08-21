using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using SimpleGit11.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class DialogValidationViewModelTests
{
    private static readonly DialogValidationMessages ValidationMessages =
        new("Required", "Selection required");

    [TestMethod]
    public void TextInput_RequiredValueControlsSubmission()
    {
        TextInputDialogViewModel viewModel = new(
            CreateTextInputRequest("", allowEmpty: false),
            ValidationMessages);

        Assert.IsTrue(viewModel.HasErrors);
        Assert.IsFalse(viewModel.CanSubmit);
        Assert.AreEqual("Required", viewModel.TextError);

        viewModel.Text = "value";

        Assert.IsFalse(viewModel.HasErrors);
        Assert.IsTrue(viewModel.CanSubmit);
        Assert.AreEqual("", viewModel.TextError);
    }

    [TestMethod]
    public void TextInput_AllowEmptySkipsRequiredValidation()
    {
        TextInputDialogViewModel viewModel = new(
            CreateTextInputRequest("", allowEmpty: true),
            ValidationMessages);

        Assert.IsFalse(viewModel.HasErrors);
        Assert.IsTrue(viewModel.CanSubmit);
    }

    [TestMethod]
    public void Branch_StartPointValidationFollowsOrphanContentMode()
    {
        OrphanBranchContentOption empty = new(OrphanBranchContentMode.Empty, "Empty");
        OrphanBranchContentOption snapshot = new(OrphanBranchContentMode.StartPointSnapshot, "Snapshot");
        GitRevisionSelectorViewModel revisionSelector = CreateRevisionSelector(
            GitRevisionKind.Commit,
            "");
        BranchCreateDialogViewModel viewModel = new(
            revisionSelector,
            [empty, snapshot],
            ValidationMessages)
        {
            BranchName = "feature/test"
        };

        Assert.AreEqual("Selection required", viewModel.StartPointError);
        Assert.IsFalse(viewModel.CanCreate);

        viewModel.IsOrphan = true;

        Assert.AreEqual("", viewModel.StartPointError);
        Assert.IsTrue(viewModel.CanCreate);

        viewModel.SelectedOrphanContentOption = snapshot;

        Assert.AreEqual("Selection required", viewModel.StartPointError);
        Assert.IsFalse(viewModel.CanCreate);
    }

    [TestMethod]
    public void Tag_AnnotatedModeRequiresMessage()
    {
        TagCreateDialogViewModel viewModel = new(
            [CreateCommit()],
            ValidationMessages)
        {
            TagName = "v1.0"
        };

        Assert.IsTrue(viewModel.CanCreate);

        viewModel.IsAnnotated = true;

        Assert.AreEqual("Required", viewModel.MessageError);
        Assert.IsFalse(viewModel.CanCreate);

        viewModel.Message = "Release";

        Assert.AreEqual("", viewModel.MessageError);
        Assert.IsTrue(viewModel.CanCreate);
    }

    [TestMethod]
    public void Worktree_NewBranchModeRequiresBranchName()
    {
        WorktreeCreateDialogViewModel viewModel = new(
            CreateRevisionSelector(GitRevisionKind.Branch, "main"),
            "D:\\worktree",
            "",
            WorktreeCreationMode.NewBranch,
            canUseExistingBranch: true,
            validationMessages: ValidationMessages);

        Assert.AreEqual("Required", viewModel.NewBranchNameError);
        Assert.IsFalse(viewModel.CanCreate);

        viewModel.NewBranchName = "feature/test";

        Assert.AreEqual("", viewModel.NewBranchNameError);
        Assert.IsTrue(viewModel.CanCreate);
    }

    [TestMethod]
    public void Worktree_DetachedCommitUpdatesOnlySuggestedPath()
    {
        GitRevisionSelectorViewModel revisionSelector = CreateRevisionSelector(
            GitRevisionKind.Commit,
            "0123456789abcdef");
        WorktreeCreateDialogViewModel viewModel = new(
            revisionSelector,
            "D:\\worktree",
            "",
            WorktreeCreationMode.Detached,
            canUseExistingBranch: true,
            validationMessages: ValidationMessages);

        revisionSelector.SelectSuggestion(new GitRevisionSuggestion(
            "0123456789abcdef",
            "0123456  Commit",
            "Today",
            "0123456"));

        Assert.AreEqual("D:\\worktree-detach-0123456", viewModel.Path);

        viewModel.Path = "D:\\custom";
        revisionSelector.SelectSuggestion(new GitRevisionSuggestion(
            "abcdef0123456789",
            "abcdef0  Other commit",
            "Yesterday",
            "abcdef0"));

        Assert.AreEqual("D:\\custom", viewModel.Path);
    }

    [TestMethod]
    public void Worktree_TagToNewBranchToleratesTransientEmptySourceSelection()
    {
        GitRevisionSelectorViewModel revisionSelector = CreateRevisionSelector(
            GitRevisionKind.Tag,
            "v1.0");
        WorktreeCreateDialogViewModel viewModel = new(
            revisionSelector,
            "D:\\worktree",
            "feature/v1.0",
            WorktreeCreationMode.Detached,
            canUseExistingBranch: false,
            validationMessages: ValidationMessages);

        revisionSelector.SelectedSourceOption = null;
        viewModel.SelectedModeIndex = (int)WorktreeCreationMode.NewBranch;

        Assert.AreEqual(GitRevisionKind.Tag, revisionSelector.SelectedKind);
        Assert.IsNotNull(revisionSelector.SelectedSourceOption);
        Assert.AreEqual("v1.0", revisionSelector.StartPoint);
    }

    private static TextInputDialogRequest CreateTextInputRequest(string value, bool allowEmpty)
    {
        return new TextInputDialogRequest(
            "Title",
            "Header",
            value,
            "Save",
            "Cancel",
            allowEmpty: allowEmpty);
    }

    private static GitCommit CreateCommit()
    {
        return new GitCommit(
            "0123456789abcdef",
            "0123456",
            "Author",
            "author@example.com",
            null,
            "Commit",
            "Commit");
    }

    private static GitRevisionSelectorViewModel CreateRevisionSelector(
        GitRevisionKind selectedKind,
        string initialValue)
    {
        RepositoryInfo repository = new(
            "D:\\repository",
            "repository",
            "main",
            mainWorktreePath: "D:\\repository");
        return new GitRevisionSelectorViewModel(
            repository,
            new TestGitService(),
            new TestLocalizationService(),
            [
                GitRevisionKind.Head,
                GitRevisionKind.Branch,
                GitRevisionKind.Tag,
                GitRevisionKind.Commit
            ],
            selectedKind,
            initialValue);
    }

    private sealed class TestRevisionService : IGitRevisionService
    {
        public Task<IReadOnlyList<GitRevisionSuggestion>> GetSuggestionsAsync(
            RepositoryInfo repository,
            GitRevisionKind kind,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<GitRevisionSuggestion>>([]);
        }

        public Task<GitResolvedRevision> ResolveAsync(
            RepositoryInfo repository,
            GitRevisionKind kind,
            string value,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new GitResolvedRevision(value, value[..7]));
        }
    }

    private sealed class TestGitService : IGitService
    {
        public IGitArchiveService Archive => throw new NotSupportedException();
        public IGitBranchService Branches => throw new NotSupportedException();
        public IGitChangeRecoveryService ChangeRecovery => throw new NotSupportedException();
        public IGitCommitService Commits => throw new NotSupportedException();
        public IGitCommitWorkflowService CommitWorkflow => throw new NotSupportedException();
        public IGitConfigService Configuration => throw new NotSupportedException();
        public IGitDiffService Diff => throw new NotSupportedException();
        public IGitHistoryService History => throw new NotSupportedException();
        public IGitReferenceDetailsService ReferenceDetails => throw new NotSupportedException();
        public IGitRevisionService Revisions { get; } = new TestRevisionService();
        public IGitRemoteService Remotes => throw new NotSupportedException();
        public IGitRepositoryDiscoveryService RepositoryDiscovery => throw new NotSupportedException();
        public IGitRepositoryOperationService RepositoryOperations => throw new NotSupportedException();
        public IGitRepositoryRepairService RepositoryRepair => throw new NotSupportedException();
        public IGitRepositorySearchService RepositorySearch => throw new NotSupportedException();
        public IGitStagingService Staging => throw new NotSupportedException();
        public IGitStashService Stashes => throw new NotSupportedException();
        public IGitStatusService Status => throw new NotSupportedException();
        public IGitTagService Tags => throw new NotSupportedException();
        public IGitWorktreeService Worktrees => throw new NotSupportedException();

        public Task ExecuteAsync(Func<Task> operation) => throw new NotSupportedException();
        public Task<GitStatusSnapshot> GetStatusAsync(RepositoryInfo repository, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitOperationState> GetOperationStateAsync(RepositoryInfo repository, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitCommit>> GetHistoryAsync(RepositoryInfo repository, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitCommitPage> GetHistoryPageAsync(RepositoryInfo repository, int skip, int count, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitCommit> GetLastCommitAsync(RepositoryInfo repository, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitBranch>> GetLocalBranchesAsync(RepositoryInfo repository, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitBranch>> GetRemoteBranchesAsync(RepositoryInfo repository, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitTag>> GetLocalTagsAsync(RepositoryInfo repository, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitRemote>> GetRemotesAsync(RepositoryInfo repository, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitWorktree>> GetWorktreesAsync(RepositoryInfo repository, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitCurrentBranchRemoteStatus> GetCurrentBranchRemoteStatusAsync(RepositoryInfo repository, GitRemote? defaultRemote, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DateTimeOffset?> GetLastFetchTimeAsync(RepositoryInfo repository, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public AppLanguage CurrentLanguage => AppLanguage.English;

        public string GetString(string resourceKey)
        {
            return resourceKey;
        }

        public void ApplyLanguage()
        {
        }

        public void SetLanguage(AppLanguage language)
        {
        }
    }
}
