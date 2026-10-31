using System.Reflection;
using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Tests.TestInfrastructure;
using SimpleGit11.ViewModels;
using Stub = SimpleGit11.Tests.ViewModels.SettingsPullSettingsTests.ServiceStub;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class HistoryEditingViewModelTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NavigateToParent_ReplacesMultipleSelectionEvenWhenParentIsAlreadyFocused(bool parentFocused)
    {
        Fixture fixture = new();
        await fixture.ViewModel.RefreshHistoryAsync();
        GitCommit child = fixture.Commits[0];
        GitCommit parent = fixture.Commits[1];
        fixture.ViewModel.SelectedCommit = child;
        CommitParentViewItem target = fixture.ViewModel.ParentCommits.Single();
        if (parentFocused) { fixture.ViewModel.SelectedCommit = parent; }
        fixture.ViewModel.SetSelectedCommits([child, parent]);
        GitCommit[]? restoredSelection = null;
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(fixture.ViewModel.SelectedCommitRow))
            {
                restoredSelection = fixture.ViewModel.SelectedCommits.ToArray();
            }
        };

        fixture.ViewModel.NavigateToParentCommitCommand.Execute(target);

        Assert.AreSame(parent, fixture.ViewModel.SelectedCommit);
        Assert.AreSame(parent, fixture.ViewModel.SelectedCommitRow!.Commit);
        Assert.AreSame(parent, fixture.ViewModel.SelectedCommits.Single());
        Assert.IsNotNull(restoredSelection);
        CollectionAssert.AreEqual(new[] { parent }, restoredSelection);
    }

    [TestMethod]
    public async Task NavigateToParent_ClearsFiltersAndSelectsOnlyPreviouslyHiddenParent()
    {
        Fixture fixture = new();
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommit = fixture.Commits[0];
        CommitParentViewItem target = fixture.ViewModel.ParentCommits.Single();
        fixture.ViewModel.SearchText = fixture.Commits[0].Title;
        fixture.ViewModel.SetSelectedCommits([fixture.Commits[0]]);
        Assert.HasCount(1, fixture.ViewModel.Commits);

        fixture.ViewModel.NavigateToParentCommitCommand.Execute(target);

        Assert.IsFalse(fixture.ViewModel.IsCommitFilterApplied);
        Assert.AreSame(fixture.Commits[1], fixture.ViewModel.SelectedCommits.Single());
        Assert.AreSame(fixture.Commits[1], fixture.ViewModel.SelectedCommit);
    }

    [TestMethod]
    public void FileContextCommands_RequireTheClickedFileInsteadOfRelyingOnSelection()
    {
        Fixture fixture = new();
        GitChangedFile file = new("src/OtherFile.cs", "Modified");
        Assert.IsNull(fixture.ViewModel.SelectedChangedFile);
        Assert.IsFalse(fixture.ViewModel.FilterByChangedFileCommand.CanExecute(null));
        Assert.IsFalse(fixture.ViewModel.ToggleChangeDisplayModeCommand.CanExecute(null));
        Assert.IsTrue(fixture.ViewModel.FilterByChangedFileCommand.CanExecute(file));
        Assert.IsTrue(fixture.ViewModel.ToggleChangeDisplayModeCommand.CanExecute(file));
    }

    [TestMethod]
    public async Task RefreshHistory_UnchangedSnapshotPreservesRowsFileAndDiffWithoutCollectionEvents()
    {
        Fixture fixture = new() { ChangedFiles = [new("first.txt", "Modified"), new("second.txt", "Modified")] };
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedChangedFile = fixture.ChangedFiles[1];
        await fixture.ViewModel.ShowDiffCommand.ExecuteAsync(null);
        GitCommit commit = fixture.ViewModel.SelectedCommit!;
        CommitBrowserRowViewItem row = fixture.ViewModel.SelectedCommitRow!;
        GitChangedFile file = fixture.ViewModel.SelectedChangedFile!;
        int diffLoads = fixture.DiffLoads;
        int changes = 0;
        fixture.ViewModel.Commits.CollectionChanged += (_, _) => changes++;
        fixture.ViewModel.CommitRows.CollectionChanged += (_, _) => changes++;
        fixture.ViewModel.ChangedFiles.CollectionChanged += (_, _) => changes++;
        fixture.LoadPageAsync = (_, _) => Task.FromResult(new GitCommitPage(
            fixture.Commits.Select(item => Commit(item.Hash, item.Title, item.ParentHashes.ToArray())).ToArray(), false));

        await fixture.ViewModel.RefreshHistoryAsync();

        Assert.AreSame(commit, fixture.ViewModel.SelectedCommit);
        Assert.AreSame(row, fixture.ViewModel.SelectedCommitRow);
        Assert.AreSame(file, fixture.ViewModel.SelectedChangedFile);
        Assert.AreEqual(diffLoads, fixture.DiffLoads);
        Assert.AreEqual(0, changes);
    }

    [TestMethod]
    public async Task RefreshHistory_ChangedReferencesPreserveSelectedFileAndImmutableCommitContent()
    {
        Fixture fixture = new() { ChangedFiles = [new("first.txt", "Modified"), new("second.txt", "Modified")] };
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedChangedFile = fixture.ChangedFiles[1];
        await fixture.ViewModel.ShowDiffCommand.ExecuteAsync(null);
        GitChangedFile file = fixture.ViewModel.SelectedChangedFile!;
        int loads = fixture.DiffLoads;
        GitCommit old = fixture.Commits[0];
        fixture.Commits[0] = new GitCommit(old.Hash, old.ShortHash, old.AuthorName, old.AuthorEmail,
            old.AuthoredAt, old.Title, old.Message, references: [new("v2", GitCommitReferenceKind.Tag)], parentHashes: old.ParentHashes);

        await fixture.ViewModel.RefreshHistoryAsync();

        Assert.AreSame(fixture.Commits[0], fixture.ViewModel.SelectedCommit);
        Assert.AreSame(file, fixture.ViewModel.SelectedChangedFile);
        Assert.AreEqual(loads, fixture.DiffLoads);
        Assert.AreEqual("v2", fixture.ViewModel.SelectedCommit!.References.Single().Name);
    }

    [TestMethod]
    public async Task RefreshHistory_FailedReadRetainsSelectedFileAndRows()
    {
        Fixture fixture = new() { ChangedFiles = [new("first.txt", "Modified"), new("second.txt", "Modified")] };
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedChangedFile = fixture.ChangedFiles[1];
        GitChangedFile file = fixture.ViewModel.SelectedChangedFile!;
        CommitBrowserRowViewItem row = fixture.ViewModel.SelectedCommitRow!;
        fixture.LoadPageAsync = (_, _) => throw new DirectoryNotFoundException();

        await fixture.ViewModel.RefreshHistoryAsync();

        Assert.AreSame(row, fixture.ViewModel.SelectedCommitRow);
        Assert.AreSame(file, fixture.ViewModel.SelectedChangedFile);
    }

    [TestMethod]
    public async Task ChangedFileTree_MirrorsLoadedHistoryAndClearsOnRepositoryChange()
    {
        GitChangedFile first = new("src/First.cs", "Modified");
        GitChangedFile second = new("docs/Help.md", "Added");
        Fixture fixture = new() { ChangedFiles = [first, second] };
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommit = fixture.Commits[0];
        await fixture.ViewModel.ShowDiffCommand.ExecuteAsync(null);

        Assert.IsFalse(fixture.ViewModel.FileTree.ShowFolders);
        CollectionAssert.AreEqual(new[] { "docs", "src" },
            fixture.ViewModel.FileTree.Roots.Select(item => item.Name).ToArray());
        Assert.AreSame(first, fixture.ViewModel.FileTree.Roots[1].Children.Single().File);
        Assert.AreSame(first, fixture.ViewModel.SelectedChangedFile);

        fixture.ViewModel.FileTree.ShowFolders = true;
        fixture.ViewModel.Receive(new RepositoryChangedMessage(null));
        Assert.IsEmpty(fixture.ViewModel.FileTree.Roots);
        Assert.IsTrue(fixture.ViewModel.FileTree.ShowFolders);
    }

    [TestMethod]
    public async Task SelectedCommitFile_LoadsDiffUsingTheHistoryFixture()
    {
        Fixture fixture = new();
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommit = fixture.Commits[0];
        fixture.ViewModel.SelectedChangedFile = new GitChangedFile("selected.txt", "A");
        await fixture.ViewModel.ShowDiffCommand.ExecuteAsync(null);

        Assert.IsTrue(fixture.DiffLoads > 0);
        Assert.IsFalse(fixture.ViewModel.IsDiffLoading);
        Assert.IsTrue(fixture.ViewModel.HasDiffEmptyState);
        Assert.AreEqual("", fixture.ViewModel.DiffText);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LoadRemainingVisibilityTracksPaginationAndHistoryEditing(bool hasMoreCommits)
    {
        Fixture fixture = new() { HasMoreCommits = hasMoreCommits };
        List<string?> changedProperties = [];
        fixture.ViewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        Assert.IsFalse(fixture.ViewModel.IsLoadRemainingHistoryVisible);
        await fixture.ViewModel.RefreshHistoryAsync();
        Assert.AreEqual(hasMoreCommits, fixture.ViewModel.IsLoadRemainingHistoryVisible);
        if (hasMoreCommits)
        {
            CollectionAssert.Contains(changedProperties, nameof(HistoryViewModel.IsLoadRemainingHistoryVisible));
        }

        changedProperties.Clear();
        fixture.ViewModel.SelectedCommit = fixture.Commits[1];
        await fixture.ViewModel.StartHistoryEditCommand.ExecuteAsync(null);
        Assert.IsTrue(fixture.ViewModel.IsHistoryEditing);
        Assert.IsFalse(fixture.ViewModel.IsLoadRemainingHistoryVisible);
        CollectionAssert.Contains(changedProperties, nameof(HistoryViewModel.IsLoadRemainingHistoryVisible));

        changedProperties.Clear();
        fixture.ViewModel.CancelHistoryEditCommand.Execute(null);
        Assert.AreEqual(hasMoreCommits, fixture.ViewModel.IsLoadRemainingHistoryVisible);
        CollectionAssert.Contains(changedProperties, nameof(HistoryViewModel.IsLoadRemainingHistoryVisible));
    }

    [TestMethod]
    public async Task TagsFilterUpdatesVisibleCommitsAndResetRestoresHistory()
    {
        Fixture fixture = new();
        await fixture.ViewModel.RefreshHistoryAsync();
        int notifications = 0;
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(HistoryViewModel.IsCommitFilterApplied)) { notifications++; }
        };
        fixture.ViewModel.IsTagsOnly = true;
        Assert.IsTrue(fixture.ViewModel.IsCommitFilterApplied);
        Assert.HasCount(0, fixture.ViewModel.Commits);
        Assert.HasCount(0, fixture.ViewModel.CommitRows);
        fixture.ViewModel.ResetCommitFiltersCommand.Execute(null);
        Assert.IsFalse(fixture.ViewModel.IsTagsOnly);
        Assert.IsFalse(fixture.ViewModel.IsCommitFilterApplied);
        Assert.HasCount(3, fixture.ViewModel.Commits);
        Assert.HasCount(3, fixture.ViewModel.CommitRows);
        Assert.IsTrue(notifications >= 2);
    }

    [TestMethod]
    public async Task RefreshHistory_PreservesFocusedCommitAndRangeUsingReloadedInstances()
    {
        Fixture fixture = new();
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommitRow = fixture.ViewModel.CommitRows[1];
        fixture.ViewModel.SetSelectedCommits([fixture.ViewModel.Commits[0], fixture.ViewModel.Commits[1]]);
        List<bool> updates = [];
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(HistoryViewModel.IsUpdatingCommitRows))
            {
                updates.Add(fixture.ViewModel.IsUpdatingCommitRows);
            }
        };
        fixture.ViewModel.CommitRows.CollectionChanged += (_, _) =>
        {
            Assert.IsTrue(fixture.ViewModel.IsUpdatingCommitRows,
                "The view must ignore transient selection loss while row collections are being rebuilt.");
        };
        fixture.Commits[0] = Commit("ccccccc", "Updated newest", "bbbbbbb");
        fixture.Commits[1] = Commit("bbbbbbb", "Updated selected", "aaaaaaa");

        await fixture.ViewModel.RefreshHistoryAsync();

        Assert.AreSame(fixture.Commits[1], fixture.ViewModel.SelectedCommit);
        Assert.AreSame(fixture.ViewModel.CommitRows[1], fixture.ViewModel.SelectedCommitRow);
        CollectionAssert.AreEquivalent(new[] { "ccccccc", "bbbbbbb" },
            fixture.ViewModel.SelectedCommits.Select(commit => commit.Hash).ToArray());
        Assert.AreSame(fixture.Commits[0], fixture.ViewModel.SelectedCommits[0]);
        Assert.AreSame(fixture.Commits[1], fixture.ViewModel.SelectedCommits[1]);
        CollectionAssert.AreEqual(new[] { true, false }, updates);
        Assert.IsFalse(fixture.ViewModel.IsUpdatingCommitRows);
    }

    [TestMethod]
    public async Task FilteringHistory_RemovesOnlyHiddenCommitsFromSelection()
    {
        Fixture fixture = new();
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommit = fixture.Commits[1];
        fixture.ViewModel.SetSelectedCommits([fixture.Commits[0], fixture.Commits[1]]);

        fixture.ViewModel.SearchText = fixture.Commits[1].Title;

        Assert.AreSame(fixture.Commits[1], fixture.ViewModel.SelectedCommit);
        Assert.AreSame(fixture.Commits[1], fixture.ViewModel.SelectedCommits.Single());
        Assert.AreSame(fixture.ViewModel.CommitRows.Single(), fixture.ViewModel.SelectedCommitRow);
        Assert.IsFalse(fixture.ViewModel.IsUpdatingCommitRows);
    }

    [TestMethod]
    public async Task CancelRestoresRangeAndSelectingAnotherCommitNotifiesStartCommand()
    {
        Fixture fixture = new();
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommit = fixture.Commits[1];
        fixture.ViewModel.SetSelectedCommits([fixture.Commits[0], fixture.Commits[1]]);
        await fixture.ViewModel.StartHistoryEditCommand.ExecuteAsync(null);
        fixture.ViewModel.SetSelectedCommits([]); // ListView clears selection while rows are replaced.
        fixture.ViewModel.CancelHistoryEditCommand.Execute(null);
        CollectionAssert.AreEquivalent(new[] { "ccccccc", "bbbbbbb" }, fixture.ViewModel.SelectedCommits.Select(commit => commit.Hash).ToArray());
        Assert.IsTrue(fixture.ViewModel.StartHistoryEditCommand.CanExecute(null));
        int notifications = 0;
        fixture.ViewModel.StartHistoryEditCommand.CanExecuteChanged += (_, _) => notifications++;
        fixture.ViewModel.SelectedCommitRow = null;
        Assert.IsFalse(fixture.ViewModel.StartHistoryEditCommand.CanExecute(null));
        fixture.ViewModel.SelectedCommitRow = fixture.ViewModel.CommitRows[2];
        Assert.IsTrue(fixture.ViewModel.StartHistoryEditCommand.CanExecute(null));
        Assert.IsTrue(notifications >= 2);
        await fixture.ViewModel.StartHistoryEditCommand.ExecuteAsync(null);
        Assert.IsTrue(fixture.ViewModel.IsHistoryEditing);
        fixture.ViewModel.CancelHistoryEditCommand.Execute(null);
    }

    [TestMethod]
    public async Task DraftDoesNotRunRebaseAndDisablesSearchGraphAndHidesRowsOutsideRange()
    {
        Fixture fixture = new();
        await fixture.StartAsync();
        Assert.IsTrue(fixture.ViewModel.IsHistoryEditing);
        Assert.IsFalse(fixture.ViewModel.IsCommitSearchEnabled);
        Assert.IsFalse(fixture.ViewModel.IsCommitGraphVisible);
        Assert.IsFalse(fixture.ViewModel.CommitRows.Any(row => row.Commit.Hash == "aaaaaaa"));
        Assert.IsFalse(fixture.ViewModel.RefreshHistoryCommand.CanExecute(null));
        Assert.IsFalse(fixture.ViewModel.RevertCommitCommand.CanExecute(null));
        Assert.AreEqual(0, fixture.Editor.RebaseCalls);
        fixture.ViewModel.DeleteDraftCommitCommand.Execute(null);
        Assert.AreEqual(2, fixture.ViewModel.CommitRows.Count);
        Assert.IsTrue(fixture.ViewModel.IsCommitContentUnavailable);
        fixture.ViewModel.CancelHistoryEditCommand.Execute(null);
        Assert.IsFalse(fixture.ViewModel.IsHistoryEditing);
        Assert.IsTrue(fixture.ViewModel.IsCommitGraphVisible);
        Assert.IsTrue(fixture.ViewModel.CommitRows.All(row => row.IsSelectable && !row.HasEditIndicator));
        Assert.AreEqual(0, fixture.Editor.RebaseCalls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task QuickDialogsDoNotPublishProgressWhileWaitingForUser(bool deleting)
    {
        Fixture fixture = new();
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommit = fixture.Commits[1];
        TaskCompletionSource<bool> reply = new();
        fixture.ConfirmationDialog = () => reply.Task;
        fixture.MessageDialog = async () => { await reply.Task; return null; };
        Task operation = deleting
            ? fixture.ViewModel.DeleteHistoryCommitCommand.ExecuteAsync(null)
            : fixture.ViewModel.EditCommitMessageCommand.ExecuteAsync(null);

        Assert.IsFalse(operation.IsCompleted);
        Assert.IsTrue(fixture.ViewModel.IsGitOperationRunning);
        Assert.IsNotNull(fixture.LastOperation);
        Assert.IsFalse(fixture.LastOperation.IsRunning);
        reply.SetResult(false);
        await operation;
        Assert.AreEqual(0, fixture.Editor.RebaseCalls);
    }

    [TestMethod]
    public async Task DraftTitleCountsOnlyEditableRangeAndRestoresNormalTitleOnCancel()
    {
        Fixture fixture = new();
        List<string?> changedProperties = [];
        fixture.ViewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        await fixture.StartAsync();
        Assert.AreEqual("Editing: 2 CommitCountMany", fixture.ViewModel.CommitsTitle);
        Assert.IsTrue(changedProperties.Contains(nameof(HistoryViewModel.CommitsTitle)));
        fixture.ViewModel.DeleteDraftCommitCommand.Execute(null);
        Assert.AreEqual("Editing: 2 CommitCountMany", fixture.ViewModel.CommitsTitle);
        fixture.ViewModel.UndoHistoryEditCommand.Execute(null);
        fixture.ViewModel.SelectedCommit = fixture.Commits[0];
        fixture.MessageAnswer = new CommitDialogResult("Combined", false);
        await fixture.ViewModel.MergeDraftWithMessageCommand.ExecuteAsync(null);
        Assert.AreEqual("Editing: 2 CommitCountMany", fixture.ViewModel.CommitsTitle);

        changedProperties.Clear();
        fixture.ViewModel.CancelHistoryEditCommand.Execute(null);
        Assert.AreEqual("3 CommitCountMany", fixture.ViewModel.CommitsTitle);
        Assert.IsTrue(changedProperties.Contains(nameof(HistoryViewModel.CommitsTitle)));
    }

    [TestMethod]
    public async Task MessageContextCommandUpdatesDraftAndDetailsWithoutChangingCommitOrHidingFiles()
    {
        Fixture fixture = new();
        await fixture.StartAsync();
        fixture.MessageAnswer = new CommitDialogResult("New title\n\nBody", false);
        await fixture.ViewModel.EditCommitMessageCommand.ExecuteAsync(null);
        Assert.AreEqual("New title\n\nBody", fixture.ViewModel.SelectedCommitMessage);
        Assert.AreEqual("B", fixture.ViewModel.SelectedCommit!.Message);
        Assert.IsFalse(fixture.ViewModel.IsCommitContentUnavailable);
        Assert.AreEqual("HistoryEditMessageChanged", fixture.ViewModel.SelectedCommitRow!.EditIndicator);
        Assert.AreEqual(0, fixture.Editor.RebaseCalls);
    }

    [TestMethod]
    public async Task CombineRequiresMessageDialogAndCancelLeavesDraftUnchanged()
    {
        Fixture fixture = new();
        await fixture.StartAsync();
        fixture.ViewModel.SelectedCommit = fixture.Commits[0];
        int dialogs = 0;
        fixture.MessageDialog = () => { dialogs++; return Task.FromResult<CommitDialogResult?>(null); };
        await fixture.ViewModel.MergeDraftWithMessageCommand.ExecuteAsync(null);
        Assert.AreEqual(1, dialogs);
        Assert.IsFalse(fixture.ViewModel.ApplyHistoryEditCommand.CanExecute(null));
        Assert.IsFalse(fixture.ViewModel.UndoHistoryEditCommand.CanExecute(null));
        Assert.IsTrue(fixture.ViewModel.CommitRows.All(row => !row.HasEditIndicator));
        Assert.AreEqual(0, fixture.Editor.RebaseCalls);
    }

    [TestMethod]
    public async Task CombiningEarlierCommitsKeepsLaterDetailsAvailableAndDeletionHidesThem()
    {
        Fixture fixture = new();
        fixture.Editor.Revisions = "ccccccc\nbbbbbbb\naaaaaaa\n";
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommit = fixture.Commits[2];
        await fixture.ViewModel.StartHistoryEditCommand.ExecuteAsync(null);
        Assert.IsTrue(fixture.ViewModel.IsHistoryEditing);
        fixture.ViewModel.SelectedCommit = fixture.Commits[1];
        fixture.MessageAnswer = new CommitDialogResult("Combined", false);
        await fixture.ViewModel.MergeDraftWithMessageCommand.ExecuteAsync(null);
        fixture.ViewModel.SelectedCommit = fixture.Commits[0];
        Assert.IsFalse(fixture.ViewModel.SelectedCommitRow!.HasEditIndicator);
        Assert.IsTrue(fixture.ViewModel.IsCommitContentAvailable);

        fixture.ViewModel.SelectedCommit = fixture.Commits[2];
        fixture.ViewModel.DeleteDraftCommitCommand.Execute(null);
        fixture.ViewModel.SelectedCommit = fixture.Commits[0];
        Assert.AreEqual("HistoryEditAffected", fixture.ViewModel.SelectedCommitRow!.EditIndicator);
        Assert.IsTrue(fixture.ViewModel.IsCommitContentUnavailable);
        fixture.ViewModel.UndoHistoryEditCommand.Execute(null);
        Assert.IsTrue(fixture.ViewModel.IsCommitContentAvailable);
        Assert.IsFalse(fixture.ViewModel.SelectedCommitRow!.HasEditIndicator);
        Assert.AreEqual(0, fixture.Editor.RebaseCalls);
    }

    [TestMethod]
    public async Task ApplySendsDraftAndPreparedMessagesAndRefreshesRealHistory()
    {
        Fixture fixture = new();
        await fixture.StartAsync();
        fixture.ViewModel.SelectedCommit = fixture.Commits[0];
        fixture.MessageAnswer = new CommitDialogResult("Combined\n\nBody", false);
        await fixture.ViewModel.MergeDraftWithMessageCommand.ExecuteAsync(null);
        Assert.AreEqual(2, fixture.ViewModel.CommitRows.Count);
        Assert.IsTrue(fixture.ViewModel.IsCommitContentUnavailable);
        Assert.AreEqual("Combined\n\nBody", fixture.ViewModel.SelectedCommitMessage);
        await fixture.ViewModel.ApplyHistoryEditCommand.ExecuteAsync(null);
        Assert.AreEqual(1, fixture.Editor.RebaseCalls);
        Assert.AreEqual("reword bbbbbbb\nfixup ccccccc\n", fixture.Editor.Plan);
        Assert.AreEqual("Combined\n\nBody\n", fixture.Editor.Messages!["bbbbbbb"]);
        Assert.IsFalse(fixture.ViewModel.IsHistoryEditing);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ApplyRejectsChangedHeadOrBranchAndKeepsDraft(bool changeHead)
    {
        Fixture fixture = new();
        await fixture.StartAsync();
        fixture.ViewModel.DeleteDraftCommitCommand.Execute(null);
        if (changeHead) { fixture.Head = Commit("ddddddd", "D", "ccccccc"); }
        else { fixture.Editor.Branch = "refs/heads/other"; }
        await fixture.ViewModel.ApplyHistoryEditCommand.ExecuteAsync(null);
        Assert.AreEqual(0, fixture.Editor.RebaseCalls);
        Assert.IsTrue(fixture.ViewModel.IsHistoryEditing);
        Assert.AreEqual("HistoryEditHistoryChanged", fixture.LastNotification!.Message);
    }

    [TestMethod]
    public async Task ConflictBannerRetainsChangesNavigationActionAfterHistoryRefresh()
    {
        Fixture fixture = new();
        await fixture.StartAsync();
        fixture.ViewModel.DeleteDraftCommitCommand.Execute(null);
        fixture.Editor.ExitCode = 1;
        fixture.HasConflicts = true;
        await fixture.ViewModel.ApplyHistoryEditCommand.ExecuteAsync(null);
        Assert.IsFalse(fixture.ViewModel.IsHistoryEditing);
        Assert.IsTrue(fixture.Window.IsNotificationOpen);
        Assert.AreEqual(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning, fixture.Window.NotificationSeverity);
        Assert.IsNotNull(fixture.Window.NotificationActionCommand);
        Assert.AreEqual("OpenChangesForConflictsButtonText", fixture.Window.NotificationActionText);
    }

    [TestMethod]
    public async Task SwitchingRepositoryDiscardsDraftAndStaleMessageDialogReply()
    {
        Fixture fixture = new();
        await fixture.StartAsync();
        TaskCompletionSource<CommitDialogResult?> answer = new();
        fixture.MessageDialog = () => answer.Task;
        Task editing = fixture.ViewModel.EditCommitMessageCommand.ExecuteAsync(null);
        fixture.Window.SetCurrentRepository(new RepositoryInfo("C:/another", "another", "main"), []);
        answer.SetResult(new CommitDialogResult("stale", false));
        await editing;
        Assert.IsFalse(fixture.ViewModel.IsHistoryEditing);
        Assert.IsNull(fixture.ViewModel.SelectedCommit);
        Assert.AreEqual(0, fixture.Editor.RebaseCalls);
    }

    private static GitCommit Commit(string hash, string title, params string[] parents) => new(hash, hash, "Author", "a@example.invalid", null, title, title,
        isSynchronized: true, parentHashes: parents);

    [TestMethod]
    public async Task MergeRangeStartsDraftWithoutAdvancedEditor()
    {
        Fixture fixture = new();
        fixture.Commits[0] = Commit("ccccccc", "Merge", "bbbbbbb", "ddddddd");
        fixture.Head = fixture.Commits[0];
        fixture.Editor.Revisions = "ccccccc\nbbbbbbb\n";
        AppNavigationTarget? target = null;
        fixture.Window.NavigationRequested += (_, args) => target = args.Target;
        await fixture.StartAsync();
        Assert.IsTrue(fixture.ViewModel.IsHistoryEditing);
        Assert.IsNull(target);
        Assert.AreEqual(2, fixture.ViewModel.CommitRows.Count);
        Assert.AreEqual(0, fixture.Editor.RebaseCalls);
    }

    [TestMethod]
    public void OpenReference_RequestsBranchesNavigationWithOriginalReference()
    {
        Fixture fixture = new();
        NavigationRequestedEventArgs? navigation = null;
        fixture.Window.NavigationRequested += (_, args) => navigation = args;
        foreach (GitCommitReferenceKind kind in Enum.GetValues<GitCommitReferenceKind>())
        {
            GitCommitReference reference = new("feature/nested/name", kind);
            fixture.ViewModel.OpenReferenceCommand.Execute(reference);
            Assert.IsNotNull(navigation);
            Assert.AreEqual(AppNavigationTarget.Branches, navigation.Target);
            Assert.AreSame(reference, navigation.Parameter);
        }

        navigation = null;
        fixture.ViewModel.OpenReferenceCommand.Execute(null);
        Assert.IsNull(navigation);
        fixture.Window.CloseCurrentRepository();
        fixture.ViewModel.OpenReferenceCommand.Execute(new("main", GitCommitReferenceKind.LocalBranch));
        Assert.IsNull(navigation);
    }

    [TestMethod]
    public async Task CancelingParentChoiceLeavesDraftUnchangedAndIgnoresStaleAnswer()
    {
        Fixture fixture = new() { ParentAnswer = null };
        fixture.Commits[1] = Commit("bbbbbbb", "Merge", "aaaaaaa", "ddddddd");
        await fixture.StartAsync();
        fixture.ViewModel.SelectedCommit = fixture.Commits[0];
        await fixture.ViewModel.MoveDraftDownCommand.ExecuteAsync(null);
        await fixture.ViewModel.MergeDraftWithMessageCommand.ExecuteAsync(null);
        Assert.AreEqual(2, fixture.ParentDialogs);
        Assert.IsFalse(fixture.ViewModel.ApplyHistoryEditCommand.CanExecute(null));
        Assert.IsFalse(fixture.ViewModel.UndoHistoryEditCommand.CanExecute(null));
        Assert.IsTrue(fixture.ViewModel.CommitRows.All(row => !row.HasEditIndicator));
        TaskCompletionSource<int?> answer = new();
        fixture.ParentDialog = () => answer.Task;
        Task moving = fixture.ViewModel.MoveDraftDownCommand.ExecuteAsync(null);
        fixture.ViewModel.CancelHistoryEditCommand.Execute(null);
        answer.SetResult(2);
        await moving;
        Assert.IsFalse(fixture.ViewModel.IsHistoryEditing);
        Assert.AreEqual(0, fixture.Editor.RebaseCalls);
    }

    [TestMethod]
    public async Task QuickMessageEditPreservesUnchangedNativeTodoLines()
    {
        Fixture fixture = new();
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommit = fixture.Commits[1];
        fixture.MessageAnswer = new CommitDialogResult("Edited", false);
        fixture.Editor.Todo = "# native comment\nlabel original\nreset original\np\tbbbbbbb B\nmerge -C ccccccc side # merge subject\nexec echo unchanged\n\n";
        await fixture.ViewModel.EditCommitMessageCommand.ExecuteAsync(null);
        Assert.AreEqual("# native comment\nlabel original\nreset original\nreword bbbbbbb\nmerge -C ccccccc side # merge subject\nexec echo unchanged\n\n", fixture.Editor.Plan);
    }

    [TestMethod]
    public async Task ExistingGitOperationDoesNotEnterDraft()
    {
        Fixture fixture = new() { OperationKind = GitOperationKind.Rebase };
        await fixture.StartAsync();
        Assert.IsFalse(fixture.ViewModel.IsHistoryEditing);
        Assert.AreEqual("HistoryEditOperationInProgress", fixture.LastNotification!.Message);
    }

    [TestMethod]
    public async Task DecliningApplyLeavesDraftUnchangedAndDoesNotRunGit()
    {
        Fixture fixture = new();
        await fixture.StartAsync();
        fixture.ViewModel.DeleteDraftCommitCommand.Execute(null);
        fixture.Confirm = false;
        await fixture.ViewModel.ApplyHistoryEditCommand.ExecuteAsync(null);
        Assert.IsTrue(fixture.ViewModel.IsHistoryEditing);
        Assert.AreEqual(0, fixture.Editor.RebaseCalls);
        Assert.AreEqual("HistoryEditDeleted", fixture.ViewModel.SelectedCommitRow!.EditIndicator);
    }

    [TestMethod]
    public async Task QuickMessageEditIsEnabledForPublishedEarlierCommitAndRunsRebaseWithoutDraft()
    {
        Fixture fixture = new();
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommit = fixture.Commits[1];
        Assert.IsTrue(fixture.ViewModel.EditCommitMessageCommand.CanExecute(null));
        fixture.MessageAnswer = new CommitDialogResult("Changed\n\nBody", false);
        await fixture.ViewModel.EditCommitMessageCommand.ExecuteAsync(null);
        Assert.IsFalse(fixture.ViewModel.IsHistoryEditing);
        Assert.AreEqual(1, fixture.Editor.RebaseCalls);
        StringAssert.Contains(fixture.Editor.Plan!, "reword bbbbbbb");
        Assert.AreEqual("Changed\n\nBody\n", fixture.Editor.Messages!["bbbbbbb"]);
        CollectionAssert.Contains(fixture.Editor.Arguments!.ToArray(), "--rebase-merges");
    }

    [TestMethod]
    public async Task QuickHeadMessageEditUsesMessageOnlyAmend()
    {
        Fixture fixture = new();
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommit = fixture.Commits[0];
        fixture.MessageAnswer = new CommitDialogResult("Changed", false);
        await fixture.ViewModel.EditCommitMessageCommand.ExecuteAsync(null);
        Assert.AreEqual(1, fixture.AmendCalls);
        Assert.IsTrue(fixture.AmendOptions!.MessageOnly);
        Assert.IsTrue(fixture.AmendOptions.AllowEmpty);
        Assert.AreEqual(0, fixture.Editor.RebaseCalls);
    }

    [TestMethod]
    public async Task QuickMergeRequestsMessageAndQuickDeleteRequiresConfirmation()
    {
        Fixture fixture = new();
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommit = fixture.Commits[0];
        fixture.MessageAnswer = new CommitDialogResult("Together", false);
        await fixture.ViewModel.MergeCommitWithPreviousCommand.ExecuteAsync(null);
        StringAssert.Contains(fixture.Editor.Plan!, "reword bbbbbbb\nfixup ccccccc");
        Assert.AreEqual("Together\n", fixture.Editor.Messages!["bbbbbbb"]);
        Assert.IsFalse(fixture.ViewModel.IsHistoryEditing);
        fixture.Confirm = false;
        await fixture.ViewModel.DeleteHistoryCommitCommand.ExecuteAsync(null);
        Assert.AreEqual(1, fixture.Editor.RebaseCalls);
        fixture.Confirm = true;
        await fixture.ViewModel.DeleteHistoryCommitCommand.ExecuteAsync(null);
        StringAssert.Contains(fixture.Editor.Plan!, "drop ccccccc");
        Assert.AreEqual(2, fixture.Editor.RebaseCalls);
        fixture.ViewModel.SelectedCommit = fixture.Commits[2];
        Assert.IsFalse(fixture.ViewModel.MergeCommitWithPreviousCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task QuickCanceledMessageAndChangedRepositoryOrBranchDoNotApply()
    {
        Fixture fixture = new();
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommit = fixture.Commits[1];
        await fixture.ViewModel.EditCommitMessageCommand.ExecuteAsync(null);
        Assert.AreEqual(0, fixture.Editor.RebaseCalls);
        fixture.MessageDialog = () =>
        {
            fixture.Editor.Branch = "refs/heads/other";
            return Task.FromResult<CommitDialogResult?>(new("Changed", false));
        };
        await fixture.ViewModel.EditCommitMessageCommand.ExecuteAsync(null);
        Assert.AreEqual(0, fixture.Editor.RebaseCalls);
        Assert.AreEqual("HistoryEditHistoryChanged", fixture.LastNotification!.Message);
        fixture.MessageDialog = () =>
        {
            fixture.Window.SetCurrentRepository(new RepositoryInfo("C:/another", "another", "main"), []);
            return Task.FromResult<CommitDialogResult?>(new("Changed again", false));
        };
        await fixture.ViewModel.EditCommitMessageCommand.ExecuteAsync(null);
        Assert.AreEqual(0, fixture.Editor.RebaseCalls);
        Assert.AreEqual(0, fixture.AmendCalls);
    }

    [TestMethod]
    public async Task QuickConflictShowsExistingChangesNavigationBanner()
    {
        Fixture fixture = new();
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommit = fixture.Commits[1];
        fixture.Editor.ExitCode = 1;
        fixture.HasConflicts = true;
        await fixture.ViewModel.DeleteHistoryCommitCommand.ExecuteAsync(null);
        Assert.IsFalse(fixture.ViewModel.IsHistoryEditing);
        Assert.AreEqual(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning, fixture.Window.NotificationSeverity);
        Assert.AreEqual("OpenChangesForConflictsButtonText", fixture.Window.NotificationActionText);
    }

    [TestMethod]
    [DataRow("message", 1)]
    [DataRow("delete", 1)]
    [DataRow("move", 1)]
    [DataRow("move", 2)]
    [DataRow("merge", 1)]
    [DataRow("merge", 2)]
    [DataRow("merge-after", 1)]
    [DataRow("merge-after", 2)]
    [DataRow("range", 1)]
    [DataRow("collapse", 1)]
    [DataRow("sequence", 2)]
    [DataRow("move-merge", 1)]
    [DataRow("move-merge", 2)]
    [DataRow("move-branch", 1)]
    [DataRow("root", 1)]
    public async Task DraftRewritesRealHistoryAcrossMerge(string action, int parent)
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("base.txt", "base");
        await repository.CommitAllAsync("base");
        await repository.RunGitAsync("switch", "-c", "topic");
        repository.WriteFile("topic.txt", "topic");
        await repository.CommitAllAsync("topic");
        await repository.RunGitAsync("switch", "main");
        repository.WriteFile("main.txt", "main");
        await repository.CommitAllAsync("main");
        await repository.RunGitAsync("merge", "--no-ff", "topic", "-m", "merge");
        repository.WriteFile("later.txt", "later");
        await repository.CommitAllAsync("later");
        await repository.RunGitAsync("config", "rebase.abbreviateCommands", "true");
        Fixture fixture = new(repository) { ParentAnswer = parent, MessageAnswer = new("Changed\n\nBody", false) };
        await fixture.ViewModel.RefreshHistoryAsync();
        GitCommit basis = fixture.ViewModel.Commits.Single(commit => commit.Title == (action == "root" ? "base" : "main"));
        fixture.ViewModel.SelectedCommit = basis;
        if (action == "range") { fixture.ViewModel.SetSelectedCommits([basis, fixture.ViewModel.Commits.Single(commit => commit.Title == "topic")]); }
        await fixture.ViewModel.StartHistoryEditCommand.ExecuteAsync(null);
        Assert.IsTrue(fixture.ViewModel.IsHistoryEditing, fixture.LastNotification?.Message);
        if (action == "range")
        {
            Assert.IsFalse(fixture.ViewModel.Commits.Any(commit => commit.Title is "merge" or "later"));
            await fixture.ViewModel.EditCommitMessageCommand.ExecuteAsync(null);
        }
        else
        {
            if (action == "sequence")
            {
                fixture.ViewModel.SelectedCommit = fixture.ViewModel.Commits.Single(commit => commit.Title == "later");
                await fixture.ViewModel.MoveDraftDownCommand.ExecuteAsync(null);
                fixture.ViewModel.UndoHistoryEditCommand.Execute(null);
                Assert.IsFalse(fixture.ViewModel.ApplyHistoryEditCommand.CanExecute(null));
                await fixture.ViewModel.MoveDraftDownCommand.ExecuteAsync(null);
            }
            if (action == "collapse")
            {
                foreach (string title in new[] { "main", "topic" })
                {
                    fixture.ViewModel.SelectedCommit = fixture.ViewModel.Commits.Single(commit => commit.Title == title);
                    fixture.ViewModel.DeleteDraftCommitCommand.Execute(null);
                }
            }
            fixture.ViewModel.SelectedCommit = fixture.ViewModel.Commits.Single(commit => commit.Title == (action is "move" or "merge-after" ? "later" : "merge"));
            if (action is "message" or "collapse" or "sequence" or "root") { await fixture.ViewModel.EditCommitMessageCommand.ExecuteAsync(null); }
            else if (action == "delete") { fixture.ViewModel.DeleteDraftCommitCommand.Execute(null); }
            else if (action is "move" or "move-merge") { await fixture.ViewModel.MoveDraftDownCommand.ExecuteAsync(null); }
            else if (action == "move-branch")
            {
                fixture.ViewModel.SelectedCommit = fixture.ViewModel.Commits.Single(commit => commit.Title == "topic");
                await fixture.ViewModel.MoveDraftUpCommand.ExecuteAsync(null);
            }
            else { await fixture.ViewModel.MergeDraftWithMessageCommand.ExecuteAsync(null); }
        }
        await fixture.ViewModel.ApplyHistoryEditCommand.ExecuteAsync(null);
        Assert.IsFalse(fixture.ViewModel.IsHistoryEditing, fixture.LastNotification?.Message);
        Assert.AreEqual("HistoryEditCompleted", fixture.LastNotification?.Message, fixture.LastNotification?.Details);
        Assert.IsFalse(repository.FileExists(".git/rebase-merge/git-rebase-todo"));
        string files = await repository.RunGitAsync("ls-tree", "--name-only", "HEAD");
        StringAssert.Contains(files, "base.txt");
        Assert.AreEqual(action != "collapse", files.Contains("main.txt"));
        StringAssert.Contains(files, "later.txt");
        Assert.AreEqual(action is not ("delete" or "collapse"), files.Contains("topic.txt"));
        Assert.AreEqual(action is "delete" or "merge" or "collapse" or "move-merge" or "move-branch" ? "0" : "1", await repository.RunGitAsync("rev-list", "--merges", "--count", "HEAD"));
        if (action is "message" or "merge" or "merge-after" or "range" or "collapse" or "sequence" or "root")
        { StringAssert.Contains(await repository.RunGitAsync("log", "--format=%B"), "Changed\n\nBody"); }
        Assert.AreEqual(action == "sequence" ? 2 : action is "move" or "merge" or "merge-after" or "move-merge" or "move-branch" ? 1 : 0, fixture.ParentDialogs);
        if (action == "move")
        {
            Assert.AreEqual("merge", await repository.RunGitAsync("log", "-1", "--format=%s"));
            Assert.AreEqual("later", await repository.RunGitAsync("log", "-1", "--format=%s", "HEAD^" + parent));
        }
    }

    [TestMethod]
    [DataRow("reword")]
    [DataRow("fixup")]
    [DataRow("drop")]
    public async Task QuickActionsRewriteRealLinearHistory(string action)
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        foreach (string name in new[] { "first", "second", "third" })
        {
            repository.WriteFile(name + ".txt", name);
            await repository.CommitAllAsync(name);
        }
        Fixture fixture = new(repository);
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommit = fixture.ViewModel.Commits.Single(commit => commit.Title == (action == "drop" ? "first" : "second"));
        fixture.MessageAnswer = new CommitDialogResult("Changed\n\nBody", false);
        if (action == "reword") { await fixture.ViewModel.EditCommitMessageCommand.ExecuteAsync(null); }
        else if (action == "fixup") { await fixture.ViewModel.MergeCommitWithPreviousCommand.ExecuteAsync(null); }
        else { await fixture.ViewModel.DeleteHistoryCommitCommand.ExecuteAsync(null); }
        Assert.IsFalse(fixture.ViewModel.IsHistoryEditing);
        Assert.AreEqual(action == "reword" ? "3" : "2", await repository.RunGitAsync("rev-list", "--count", "HEAD"));
        Assert.AreEqual("third", await repository.RunGitAsync("log", "-1", "--format=%s"));
        if (action != "drop") { StringAssert.Contains(await repository.RunGitAsync("log", "--format=%B"), "Changed\n\nBody"); }
        Assert.AreEqual(action != "drop", (await repository.RunGitAsync("ls-tree", "--name-only", "HEAD")).Contains("first.txt"));
    }

    [TestMethod]
    [DataRow("reword")]
    [DataRow("drop")]
    [DataRow("merge-source")]
    [DataRow("merge-target")]
    public async Task QuickActionsHandleMergeCommitsWithRealGit(string action)
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("base.txt", "base");
        await repository.CommitAllAsync("base");
        await repository.RunGitAsync("switch", "-c", "topic");
        repository.WriteFile("topic.txt", "topic");
        await repository.CommitAllAsync("topic");
        await repository.RunGitAsync("switch", "main");
        repository.WriteFile("main.txt", "main");
        await repository.CommitAllAsync("main");
        await repository.RunGitAsync("merge", "--no-ff", "topic", "-m", "merge");
        await repository.RunGitAsync("config", "rebase.abbreviateCommands", "true");
        repository.WriteFile("later.txt", "later");
        await repository.CommitAllAsync("later");
        Fixture fixture = new(repository);
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommit = fixture.ViewModel.Commits.Single(commit => commit.Title == (action == "merge-target" ? "later" : "merge"));
        fixture.MessageAnswer = new CommitDialogResult("Changed\n\nBody", false);
        if (action == "reword") { await fixture.ViewModel.EditCommitMessageCommand.ExecuteAsync(null); }
        else if (action == "drop") { await fixture.ViewModel.DeleteHistoryCommitCommand.ExecuteAsync(null); }
        else { await fixture.ViewModel.MergeCommitWithPreviousCommand.ExecuteAsync(null); }
        Assert.IsFalse(fixture.ViewModel.IsHistoryEditing);
        string history = await repository.RunGitAsync("log", "--format=%B");
        if (action != "drop") { StringAssert.Contains(history, "Changed\n\nBody"); }
        Assert.AreEqual(action is "reword" or "merge-target" ? "1" : "0", await repository.RunGitAsync("rev-list", "--merges", "--count", "HEAD"));
        string files = await repository.RunGitAsync("ls-tree", "--name-only", "HEAD");
        StringAssert.Contains(files, "main.txt");
        StringAssert.Contains(files, "later.txt");
        Assert.AreEqual(action != "drop", files.Contains("topic.txt"));
        Assert.IsFalse(repository.FileExists(".git/rebase-merge/git-rebase-todo"));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task QuickHeadAmendRejectsHistoryChangedDuringMessageDialog(bool changeHead)
    {
        Fixture fixture = new();
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommit = fixture.Commits[0];
        fixture.MessageDialog = () =>
        {
            if (changeHead) { fixture.Head = Commit("ddddddd", "D", "ccccccc"); }
            else { fixture.Editor.Branch = "refs/heads/other"; }
            return Task.FromResult<CommitDialogResult?>(new("Changed", false));
        };
        await fixture.ViewModel.EditCommitMessageCommand.ExecuteAsync(null);
        Assert.AreEqual(0, fixture.AmendCalls);
        Assert.AreEqual(0, fixture.Editor.RebaseCalls);
        Assert.AreEqual("HistoryEditHistoryChanged", fixture.LastNotification!.Message);
    }

    [TestMethod]
    public async Task QuickActionDoesNotRunDuringExistingOperationOrForNonAncestor()
    {
        Fixture fixture = new() { OperationKind = GitOperationKind.Rebase };
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommit = fixture.Commits[1];
        await fixture.ViewModel.DeleteHistoryCommitCommand.ExecuteAsync(null);
        Assert.AreEqual(0, fixture.Editor.RebaseCalls);
        Assert.AreEqual("HistoryEditOperationInProgress", fixture.LastNotification!.Message);
        fixture.OperationKind = GitOperationKind.None;
        fixture.Editor.IsAncestor = false;
        await fixture.ViewModel.DeleteHistoryCommitCommand.ExecuteAsync(null);
        Assert.AreEqual(0, fixture.Editor.RebaseCalls);
        Assert.AreEqual("HistoryQuickNotAncestor", fixture.LastNotification!.Message);
    }

    [TestMethod]
    public async Task QuickMergeLoadsPreviousCommitOutsideLoadedHistoryPage()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        foreach (string name in new[] { "first", "second", "third" })
        {
            repository.WriteFile(name + ".txt", name);
            await repository.CommitAllAsync(name);
        }
        await repository.RunGitAsync("update-ref", "refs/remotes/origin/main", "HEAD^");
        Fixture fixture = new(repository) { NativePageSize = 1, Confirm = false };
        await fixture.ViewModel.RefreshHistoryAsync();
        fixture.ViewModel.SelectedCommit = fixture.ViewModel.Commits.Single();
        fixture.MessageAnswer = new CommitDialogResult("Together", false);
        await fixture.ViewModel.MergeCommitWithPreviousCommand.ExecuteAsync(null);
        Assert.AreEqual("3", await repository.RunGitAsync("rev-list", "--count", "HEAD"));
        fixture.Confirm = true;
        await fixture.ViewModel.MergeCommitWithPreviousCommand.ExecuteAsync(null);
        Assert.AreEqual("2", await repository.RunGitAsync("rev-list", "--count", "HEAD"));
        Assert.AreEqual("Together", await repository.RunGitAsync("log", "-1", "--format=%B"));
        Assert.AreEqual("first.txt\nsecond.txt\nthird.txt", await repository.RunGitAsync("ls-tree", "--name-only", "HEAD"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LoadRemaining_LoadsEveryPageAndPreservesSelectionAndFilters(bool useRange)
    {
        Fixture fixture = new();
        GitCommit[] commits = Enumerable.Range(0, 605)
            .Select(index => Commit($"{index:x40}", $"Commit {index}")).ToArray();
        List<int> offsets = [];
        fixture.LoadPageAsync = (offset, count) =>
        {
            offsets.Add(offset);
            Assert.AreEqual(300, count);
            return Task.FromResult(new GitCommitPage(commits.Skip(offset).Take(count).ToArray(), offset + count < commits.Length));
        };
        CommitBrowserViewModelBase browser = useRange ? fixture.RangeViewModel : fixture.ViewModel;
        if (useRange)
        {
            await fixture.RangeViewModel.InitializeAsync(new RevisionRangeNavigationArgs("Range", "", "", "main..feature"));
        }
        else
        {
            await fixture.ViewModel.RefreshHistoryAsync();
        }
        Assert.HasCount(300, browser.Commits);
        browser.SelectedCommit = commits[10];
        browser.SetSelectedCommits([commits[10], commits[11]]);
        browser.SearchText = "Commit 1";
        int rowUpdates = 0;
        browser.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(browser.IsUpdatingCommitRows) && browser.IsUpdatingCommitRows) { rowUpdates++; }
        };

        if (useRange) { await fixture.RangeViewModel.LoadRemainingCommitsCommand.ExecuteAsync(null); }
        else { await fixture.ViewModel.LoadRemainingHistoryCommand.ExecuteAsync(null); }

        CollectionAssert.AreEqual(new[] { 0, 300, 600 }, offsets);
        Assert.AreEqual("Commit 1", browser.SearchText);
        Assert.AreSame(commits[10], browser.SelectedCommit);
        CollectionAssert.AreEqual(new[] { commits[10], commits[11] }, browser.SelectedCommits.ToArray());
        Assert.AreEqual(1, rowUpdates);
        Assert.IsFalse(useRange ? fixture.RangeViewModel.HasMoreCommits : fixture.ViewModel.HasMoreCommits);
        Assert.IsFalse(useRange ? fixture.RangeViewModel.LoadRemainingCommitsCommand.CanExecute(null) : fixture.ViewModel.LoadRemainingHistoryCommand.CanExecute(null));
        browser.ResetCommitFiltersCommand.Execute(null);
        CollectionAssert.AreEqual(commits, browser.Commits.ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LoadRemaining_FailedLaterPageCanBeRetriedWithoutSkippingCommits(bool useRange)
    {
        Fixture fixture = new();
        GitCommit[] commits = Enumerable.Range(0, 605)
            .Select(index => Commit($"{index:x40}", $"Commit {index}")).ToArray();
        bool fail = true;
        List<int> offsets = [];
        fixture.LoadPageAsync = (offset, count) =>
        {
            offsets.Add(offset);
            if (offset == 600 && fail) { throw new DirectoryNotFoundException(); }
            return Task.FromResult(new GitCommitPage(commits.Skip(offset).Take(count).ToArray(), offset + count < commits.Length));
        };
        CommitBrowserViewModelBase browser = useRange ? fixture.RangeViewModel : fixture.ViewModel;
        if (useRange) { await fixture.RangeViewModel.InitializeAsync(new RevisionRangeNavigationArgs("Range", "", "", "main..feature")); }
        else { await fixture.ViewModel.RefreshHistoryAsync(); }

        if (useRange) { await fixture.RangeViewModel.LoadRemainingCommitsCommand.ExecuteAsync(null); }
        else { await fixture.ViewModel.LoadRemainingHistoryCommand.ExecuteAsync(null); }

        Assert.HasCount(300, browser.Commits);
        Assert.IsTrue(useRange ? fixture.RangeViewModel.LoadRemainingCommitsCommand.CanExecute(null) : fixture.ViewModel.LoadRemainingHistoryCommand.CanExecute(null));
        Assert.IsNotNull(fixture.LastNotification);
        fail = false;
        if (useRange) { await fixture.RangeViewModel.LoadRemainingCommitsCommand.ExecuteAsync(null); }
        else { await fixture.ViewModel.LoadRemainingHistoryCommand.ExecuteAsync(null); }

        CollectionAssert.AreEqual(new[] { 0, 300, 600, 300, 600 }, offsets);
        CollectionAssert.AreEqual(commits, browser.Commits.ToArray());
    }

    [TestMethod]
    public async Task RefreshHistory_AfterLoadingRemaining_PreservesFullHistorySelectionAndFilters()
    {
        Fixture fixture = new();
        List<GitCommit> commits = Enumerable.Range(0, 605)
            .Select(index => Commit($"{index:x40}", $"Commit {index}")).ToList();
        List<int> offsets = [];
        fixture.LoadPageAsync = (offset, count) =>
        {
            offsets.Add(offset);
            return Task.FromResult(new GitCommitPage(commits.Skip(offset).Take(count).ToArray(), offset + count < commits.Count));
        };
        await fixture.ViewModel.RefreshHistoryAsync();
        await fixture.ViewModel.LoadRemainingHistoryCommand.ExecuteAsync(null);
        fixture.ViewModel.SelectedCommit = commits[600];
        fixture.ViewModel.SetSelectedCommits([commits[600], commits[601]]);
        fixture.ViewModel.SearchText = "Commit 60";
        commits.Insert(0, Commit("new", "New commit"));
        offsets.Clear();

        // Navigating back through the main menu invokes this same refresh entry point.
        await fixture.ViewModel.RefreshHistoryAsync();

        CollectionAssert.AreEqual(new[] { 0, 300, 600 }, offsets);
        Assert.AreEqual("Commit 60", fixture.ViewModel.SearchText);
        Assert.AreSame(commits[601], fixture.ViewModel.SelectedCommit);
        CollectionAssert.AreEqual(new[] { commits[601], commits[602] }, fixture.ViewModel.SelectedCommits.ToArray());
        Assert.IsFalse(fixture.ViewModel.HasMoreCommits);
        Assert.IsFalse(fixture.ViewModel.IsLoadRemainingHistoryVisible);
        fixture.ViewModel.ResetCommitFiltersCommand.Execute(null);
        CollectionAssert.AreEqual(commits, fixture.ViewModel.Commits.ToArray());
    }

    [TestMethod]
    public async Task RefreshHistory_AfterRepositoryChange_RestoresTheInitialPageLimit()
    {
        Fixture fixture = new();
        GitCommit[] commits = Enumerable.Range(0, 605)
            .Select(index => Commit($"{index:x40}", $"Commit {index}")).ToArray();
        List<int> offsets = [];
        fixture.LoadPageAsync = (offset, count) =>
        {
            offsets.Add(offset);
            return Task.FromResult(new GitCommitPage(commits.Skip(offset).Take(count).ToArray(), offset + count < commits.Length));
        };
        await fixture.ViewModel.RefreshHistoryAsync();
        await fixture.ViewModel.LoadRemainingHistoryCommand.ExecuteAsync(null);
        fixture.Window.SetCurrentRepository(new RepositoryInfo(Environment.CurrentDirectory, "other", "main"), []);
        offsets.Clear();

        await fixture.ViewModel.RefreshHistoryAsync();

        CollectionAssert.AreEqual(new[] { 0 }, offsets);
        Assert.HasCount(300, fixture.ViewModel.Commits);
        Assert.IsTrue(fixture.ViewModel.HasMoreCommits);
        Assert.IsTrue(fixture.ViewModel.IsLoadRemainingHistoryVisible);
    }

    [TestMethod]
    public async Task RefreshHistory_FailedFullRefresh_RetainsLoadedHistoryAndRetriesAllPages()
    {
        Fixture fixture = new();
        GitCommit[] commits = Enumerable.Range(0, 605)
            .Select(index => Commit($"{index:x40}", $"Commit {index}")).ToArray();
        bool fail = false;
        List<int> offsets = [];
        fixture.LoadPageAsync = (offset, count) =>
        {
            offsets.Add(offset);
            if (fail && offset == 300) { throw new DirectoryNotFoundException(); }
            return Task.FromResult(new GitCommitPage(commits.Skip(offset).Take(count).ToArray(), offset + count < commits.Length));
        };
        await fixture.ViewModel.RefreshHistoryAsync();
        await fixture.ViewModel.LoadRemainingHistoryCommand.ExecuteAsync(null);
        fail = true;

        await fixture.ViewModel.RefreshHistoryAsync();

        CollectionAssert.AreEqual(commits, fixture.ViewModel.Commits.ToArray());
        Assert.IsFalse(fixture.ViewModel.HasMoreCommits);
        fail = false;
        offsets.Clear();
        await fixture.ViewModel.RefreshHistoryAsync();
        CollectionAssert.AreEqual(new[] { 0, 300, 600 }, offsets);
        CollectionAssert.AreEqual(commits, fixture.ViewModel.Commits.ToArray());
    }

    private sealed class Fixture
    {
        public GitCommit[] Commits { get; } = [Commit("ccccccc", "C", "bbbbbbb"), Commit("bbbbbbb", "B", "aaaaaaa"), Commit("aaaaaaa", "A")];
        public GitCommit Head { get; set; }
        public EditorStub Editor { get; } = new();
        public HistoryViewModel ViewModel { get; }
        public CommitRangeViewModel RangeViewModel { get; }
        public Func<int, int, Task<GitCommitPage>>? LoadPageAsync { get; set; }
        public MainWindowViewModel Window { get; }
        public CommitDialogResult? MessageAnswer { get; set; }
        public Func<Task<CommitDialogResult?>>? MessageDialog { get; set; }
        public AppNotificationMessage? LastNotification { get; private set; }
        public bool HasConflicts { get; set; }
        public GitOperationKind OperationKind { get; set; }
        public Func<Task<int?>>? ParentDialog { get; set; }
        public int? ParentAnswer { get; set; } = 1;
        public int ParentDialogs { get; private set; }
        public bool Confirm { get; set; } = true;
        public Func<Task<bool>>? ConfirmationDialog { get; set; }
        public AppOperationMessage? LastOperation { get; private set; }
        public int AmendCalls { get; private set; }
        public GitCommitOptions? AmendOptions { get; private set; }
        public int NativePageSize { get; set; } = 300;
        public bool HasMoreCommits { get; set; }
        public int DiffLoads { get; private set; }
        public IReadOnlyList<GitChangedFile> ChangedFiles { get; set; } = [];

        public Fixture(TemporaryGitRepository? nativeRepository = null)
        {
            Head = Commits[0];
            StrongReferenceMessenger messenger = new();
            messenger.Register<AppOperationMessage>(this, (_, message) => LastOperation = message);
            messenger.Register<AppNotificationMessage>(this, (_, message) => { if (!string.IsNullOrEmpty(message.Message)) { LastNotification = message; } });
            ILocalizationService strings = Stub.Create<ILocalizationService>((method, args) => method == "get_CurrentLanguage" ? AppLanguage.English : args![0] is "HistoryEditingCommitsTitle" ? "Editing: {0}" : args[0] is "HistoryEditMergeHint" or "HistoryEditMergedInto" ? "{0} {1}" : args[0]);
            GitCommandRunner? runner = nativeRepository is null ? null : new GitCommandRunner();
            GitHistoryService? nativeHistory = runner is null ? null : new GitHistoryService(runner);
            IGitEditorService editor = Editor;
            if (runner is not null)
            {
                DirectoryInfo? root = new(AppContext.BaseDirectory);
                while (root is not null && !File.Exists(Path.Combine(root.FullName, "SimpleGit11.slnx"))) { root = root.Parent; }
                Assert.IsNotNull(root);
                string configuration = typeof(GitEditorService).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
                string helper = Path.Combine(root.FullName, "SimpleGit11", "bin", "x64", configuration, "net8.0-windows10.0.19041.0", "win-x64", "SimpleGit11.GitEditor.exe");
                editor = new GitEditorService(runner, (_, _) => Task.FromResult<string?>(null), helperPath: helper);
            }
            IGitCommitService commitService = runner is null ? Stub.Create<IGitCommitService>((method, args) =>
            {
                Assert.AreEqual("AmendAsync", method);
                AmendCalls++;
                AmendOptions = (GitCommitOptions)args![2]!;
                return Task.FromResult("");
            }) : new GitCommitService(runner);
            IGitHistoryService history = nativeHistory ?? Stub.Create<IGitHistoryService>((method, _) => method == "GetChangedFilesAsync"
                ? Task.FromResult(ChangedFiles) : throw new NotSupportedException(method));
            ISettingsService settings = Stub.Create<ISettingsService>((_, _) => new AppSettings());
            IGitDiffService diff = Stub.Create<IGitDiffService>((method, _) =>
            {
                if (method != "GetCommitDiffAsync") { throw new NotSupportedException(method); }
                DiffLoads++;
                return Task.FromResult(new DiffResult([], false, true, ""));
            });
            IGitRemoteService remotes = Stub.Create<IGitRemoteService>((method, args) => method == "GetCommitsPageAsync"
                ? LoadPageAsync!((int)args![2]!, (int)args[3]!) : throw new NotSupportedException(method));
            IGitService git = Stub.Create<IGitService>((method, args) => method switch
            {
                "get_Remotes" => remotes,
                "get_Editor" => editor,
                "get_Commits" => commitService,
                "get_History" => history,
                "get_Diff" => diff,
                "ExecuteAsync" => ((Func<Task>)args![0]!)(),
                "GetOperationStateAsync" => Task.FromResult(new GitOperationState(OperationKind)),
                "GetLastCommitAsync" => nativeHistory?.GetLastCommitAsync(nativeRepository!.Repository) ?? Task.FromResult(Head),
                "GetRemainingHistoryAsync" => ReadRemainingAsync((int)args![1]!, nativeHistory, nativeRepository),
                "GetHistoryPageAsync" => LoadPageAsync?.Invoke((int)args![1]!, (int)args[2]!)
                    ?? nativeHistory?.GetCommitsPageAsync(nativeRepository!.Repository, 0, NativePageSize)
                    ?? Task.FromResult(new GitCommitPage(Commits, HasMoreCommits)),
                "GetStatusAsync" => Task.FromResult(new GitStatusSnapshot([], [], HasConflicts ? [new GitChangedFile("conflict.txt", "UU")] : [])),
                _ => throw new NotSupportedException(method)
            });
            Window = new MainWindowViewModel(Stub.Create<IRecentRepositoriesService>((_, _) => Array.Empty<RepositoryInfo>()), strings, git,
                Stub.Create<IClipboardService>(), new TestProductInfoService(), messenger, settings);
            Window.SetCurrentRepository(nativeRepository?.Repository ?? new RepositoryInfo(Environment.CurrentDirectory, "repo", "main"), []);
            RangeViewModel = new CommitRangeViewModel(Window, git, strings, Stub.Create<IClipboardService>(),
                Stub.Create<IAsyncCommandExecutor>((_, args) => ((Func<Task>)args![0]!)()),
                Stub.Create<IDialogService>(), settings, messenger);
            ViewModel = new HistoryViewModel(Window, null!, git, strings, Stub.Create<IClipboardService>(),
                Stub.Create<IAsyncCommandExecutor>((_, args) => ((Func<Task>)args![0]!)()),
                Stub.Create<IDialogService>((method, _) => method switch
                {
                    "ConfirmAsync" => ConfirmationDialog?.Invoke() ?? Task.FromResult(Confirm),
                    "ShowCommitDialogAsync" => MessageDialog?.Invoke() ?? Task.FromResult(MessageAnswer),
                    "ShowHistoryParentDialogAsync" => SelectParent(),
                    _ => throw new NotSupportedException(method)
                }), settings, messenger);
        }

        private async Task<IReadOnlyList<GitCommit>> ReadRemainingAsync(int skip, GitHistoryService? history, TemporaryGitRepository? repository)
        {
            if (LoadPageAsync is null)
            {
                return history is null ? Commits.Skip(skip).ToArray() : await history.GetRemainingCommitsAsync(repository!.Repository, skip);
            }
            List<GitCommit> commits = [];
            GitCommitPage page;
            do
            {
                page = await LoadPageAsync(skip, 300);
                commits.AddRange(page.Commits);
                skip += page.Commits.Count;
            } while (page.HasMore && page.Commits.Count > 0);
            return commits;
        }

        private Task<int?> SelectParent() { ParentDialogs++; return ParentDialog?.Invoke() ?? Task.FromResult(ParentAnswer); }

        public async Task StartAsync()
        {
            await ViewModel.RefreshHistoryAsync();
            ViewModel.SelectedCommit = Commits[1];
            await ViewModel.StartHistoryEditCommand.ExecuteAsync(null);
        }
    }

    private sealed class EditorStub : IGitEditorService
    {
        public string Branch { get; set; } = "refs/heads/main";
        public int RebaseCalls { get; private set; }
        public int ExitCode { get; set; }
        public bool IsAncestor { get; set; } = true;
        public string Revisions { get; set; } = "ccccccc\nbbbbbbb\n";
        public string Todo { get; set; } = "pick bbbbbbb B\npick ccccccc C\n# help\n";
        public string? Plan { get; private set; }
        public IReadOnlyList<string>? Arguments { get; private set; }
        public IReadOnlyDictionary<string, string>? Messages { get; private set; }
        public async Task<GitCommandResult> RunAsync(RepositoryInfo repository, IReadOnlyList<string> arguments,
            Func<bool, string, Task<string?>>? editText = null, CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? preparedMessages = null)
        {
            if (arguments[0] == "rev-parse") { return new GitCommandResult(0, Branch, ""); }
            if (arguments[0] == "rev-list") { return new GitCommandResult(0, Revisions, ""); }
            if (arguments[0] == "merge-base") { return new GitCommandResult(IsAncestor ? 0 : 1, "", ""); }
            RebaseCalls++;
            Arguments = arguments;
            Messages = preparedMessages;
            Plan = await editText!(true, Todo);
            return new GitCommandResult(ExitCode, "", ExitCode == 0 ? "" : "conflict");
        }
    }
}
