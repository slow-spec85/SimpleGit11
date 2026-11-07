using System.Collections.Specialized;
using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using SimpleGit11.Tests.TestInfrastructure;
using SimpleGit11.ViewModels;
using Stub = SimpleGit11.Tests.ViewModels.SettingsPullSettingsTests.ServiceStub;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class PageRefreshViewModelTests
{
    [TestMethod]
    public async Task Changes_UnchangedStatusPreservesRowsSelectionAndDiffButReadsFurtherFileEdits()
    {
        Fixture fixture = new();
        await fixture.Changes.RefreshStatusAsync();
        GitChangedFile selected = fixture.Changes.UnstagedChanges[1];
        fixture.Changes.SetSelectedChanges([selected], selected);
        await fixture.Changes.ShowDiffCommand.ExecuteAsync(null);
        int notifications = 0;
        fixture.Changes.VisibleChanges.CollectionChanged += (_, _) => notifications++;
        fixture.Changes.DiffLines.CollectionChanged += (_, _) => notifications++;

        await fixture.Changes.RefreshStatusAsync();
        // Wait behind the diff read enqueued by refresh, using the real serial operation queue.
        await fixture.Queue.EnqueueAsync(() => Task.CompletedTask);

        Assert.AreSame(selected, fixture.Changes.SelectedChange);
        Assert.AreSame(selected, fixture.Changes.SelectedChanges.Single());
        Assert.AreEqual(0, notifications);
        fixture.DiffText = "further edit to the already modified file";
        await fixture.Changes.RefreshStatusAsync();
        await fixture.Queue.EnqueueAsync(() => Task.CompletedTask);
        StringAssert.Contains(fixture.Changes.DiffText, fixture.DiffText);
    }

    [TestMethod]
    public async Task Changes_FailedStatusRetainsSelectionAndDisplayedDiff()
    {
        Fixture fixture = new();
        await fixture.Changes.RefreshStatusAsync();
        GitChangedFile selected = fixture.Changes.UnstagedChanges[1];
        fixture.Changes.SetSelectedChanges([selected], selected);
        await fixture.Changes.ShowDiffCommand.ExecuteAsync(null);
        string text = fixture.Changes.DiffText;
        fixture.FailStatus = true;

        await fixture.Changes.RefreshStatusAsync();

        Assert.AreSame(selected, fixture.Changes.SelectedChange);
        Assert.AreEqual(text, fixture.Changes.DiffText);
        Assert.HasCount(2, fixture.Changes.VisibleChanges);
    }

    [TestMethod]
    public async Task Changes_DeletedSelectedFileClearsSelectionAndDiff()
    {
        Fixture fixture = new();
        await fixture.Changes.RefreshStatusAsync();
        GitChangedFile selected = fixture.Changes.UnstagedChanges[1];
        fixture.Changes.SetSelectedChanges([selected], selected);
        await fixture.Changes.ShowDiffCommand.ExecuteAsync(null);
        fixture.IncludeSecondFile = false;

        await fixture.Changes.RefreshStatusAsync();

        Assert.IsNull(fixture.Changes.SelectedChange);
        Assert.IsEmpty(fixture.Changes.SelectedChanges);
        Assert.AreEqual("", fixture.Changes.DiffText);
        Assert.HasCount(1, fixture.Changes.VisibleChanges);
    }

    [TestMethod]
    public async Task Branches_UnchangedRefreshRetainsSelectedBranchAndTreeNodes()
    {
        Fixture fixture = new();
        await fixture.Branches.RefreshBranchesLocalAsync();
        GitBranch selected = fixture.Branches.SelectedBranch!;
        ReferenceTreeItemViewModel node = fixture.Branches.SelectedBranchTreeItem!;
        int resets = 0;
        fixture.Branches.BranchTree.CollectionChanged += (_, args) =>
        { if (args.Action == NotifyCollectionChangedAction.Reset) { resets++; } };

        await fixture.Branches.RefreshBranchesLocalAsync();

        Assert.AreSame(selected, fixture.Branches.SelectedBranch);
        Assert.AreSame(node, fixture.Branches.SelectedBranchTreeItem);
        Assert.AreEqual(0, resets);
    }

    [TestMethod]
    public async Task Repository_UnchangedRefreshPreservesCardsAndChangedWorktreeReplacesOnlyItsCard()
    {
        Fixture fixture = new();
        await fixture.Repository.RefreshCurrentRepositoryAsync();
        WorktreeViewItem worktree = fixture.Repository.Worktrees.Single();
        RemoteViewItem remote = fixture.Repository.RemoteViewItems.Single();
        SubmoduleViewItem submodule = fixture.Repository.Submodules.Single();
        int resets = 0;
        fixture.Repository.Worktrees.CollectionChanged += (_, args) =>
        { if (args.Action == NotifyCollectionChangedAction.Reset) { resets++; } };

        await fixture.Repository.RefreshCurrentRepositoryAsync();

        Assert.AreSame(worktree, fixture.Repository.Worktrees.Single());
        Assert.AreSame(remote, fixture.Repository.RemoteViewItems.Single());
        Assert.AreSame(submodule, fixture.Repository.Submodules.Single());
        fixture.HeadHash = "updated";
        await fixture.Repository.RefreshCurrentRepositoryAsync();
        Assert.AreNotSame(worktree, fixture.Repository.Worktrees.Single());
        Assert.AreSame(remote, fixture.Repository.RemoteViewItems.Single());
        Assert.AreSame(submodule, fixture.Repository.Submodules.Single());
        Assert.AreEqual(0, resets);
    }

    [TestMethod]
    public async Task Branches_ExpandedWorktreeDetailsCanRefreshWithoutChangingSelectedBranch()
    {
        Fixture fixture = new();
        await fixture.Branches.RefreshBranchesLocalAsync();
        await fixture.Branches.EnsureSelectedBranchWorktreesLoadedAsync();
        IReadOnlyList<GitWorktree> previous = fixture.Branches.SelectedBranchWorktrees!;
        await fixture.Branches.EnsureSelectedBranchWorktreesLoadedAsync(forceRefresh: true);
        Assert.AreSame(previous, fixture.Branches.SelectedBranchWorktrees);
        fixture.HeadHash = "updated";

        await fixture.Branches.EnsureSelectedBranchWorktreesLoadedAsync(forceRefresh: true);

        Assert.AreEqual("updated", fixture.Branches.SelectedBranchWorktrees!.Single().HeadHash);
    }

    [TestMethod]
    public async Task Repository_FailedWorktreeReadRetainsDisplayedCards()
    {
        Fixture fixture = new();
        await fixture.Repository.RefreshCurrentRepositoryAsync();
        WorktreeViewItem previous = fixture.Repository.Worktrees.Single();
        fixture.FailWorktrees = true;

        await fixture.Repository.RefreshCurrentRepositoryAsync();

        Assert.AreSame(previous, fixture.Repository.Worktrees.Single());
    }

    private sealed class Fixture
    {
        public GitOperationQueue Queue { get; } = new();
        public ChangesViewModel Changes { get; }
        public BranchesViewModel Branches { get; }
        public RepositoryViewModel Repository { get; }
        public string HeadHash { get; set; } = "initial";
        public bool FailWorktrees { get; set; }
        public bool IncludeSecondFile { get; set; } = true;
        public bool FailStatus { get; set; }
        public string DiffText { get; set; } = "original changed text";
        public Fixture()
        {
            StrongReferenceMessenger messenger = new();
            ILocalizationService strings = Stub.Create<ILocalizationService>((method, args) =>
                method == "get_CurrentLanguage" ? AppLanguage.English : args![0]);
            IGitHistoryService history = Stub.Create<IGitHistoryService>((_, _) => Task.FromResult(true));
            IGitStashService stashes = Stub.Create<IGitStashService>((_, _) => Task.FromResult<IReadOnlyList<GitStash>>([]));
            IGitDiffService diff = Stub.Create<IGitDiffService>((_, _) => Task.FromResult(
                new DiffResult([new DiffLine(DiffText, DiffLineKind.Added, "+")], false, false, "")));
            IGitConfigService configuration = Stub.Create<IGitConfigService>((method, _) => method switch
            {
                "GetUserNameAsync" or "GetUserEmailAsync" => Task.FromResult("user"),
                "GetBranchDescriptionsAsync" => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()),
                _ => throw new NotSupportedException(method)
            });
            IGitRepositorySearchService search = Stub.Create<IGitRepositorySearchService>((method, _) => method switch
            {
                "LoadStartPath" => "",
                "SaveStartPath" => null,
                "LoadFoundRepositories" => Array.Empty<RepositoryInfo>(),
                _ => throw new NotSupportedException(method)
            });
            IGitReferenceDetailsService referenceDetails = Stub.Create<IGitReferenceDetailsService>((method, _) => method switch
            {
                "GetBranchWorktreesAsync" => Task.FromResult<IReadOnlyList<GitWorktree>>([new(Environment.CurrentDirectory, HeadHash, "main", false, false, false, false)]),
                _ => throw new NotSupportedException(method)
            });
            IGitSubmoduleService submodules = Stub.Create<IGitSubmoduleService>((_, _) => Task.FromResult<IReadOnlyList<GitSubmodule>>(
                [new("module", "module", Path.Combine(Environment.CurrentDirectory, "module"), "url", "main", "hash", "hash", "hash", true, false, false, false, "", [])]));
            IGitService git = Stub.Create<IGitService>((method, args) => method switch
            {
                "ExecuteAsync" => Queue.EnqueueAsync((Func<Task>)args![0]!, (bool)args[1]!),
                "get_History" => history,
                "get_Stashes" => stashes,
                "get_Diff" => diff,
                "get_Configuration" => configuration,
                "get_RepositorySearch" => search,
                "get_Submodules" => submodules,
                "get_ReferenceDetails" => referenceDetails,
                "GetWorktreesAsync" => FailWorktrees ? Task.FromException<IReadOnlyList<GitWorktree>>(new DirectoryNotFoundException()) : Task.FromResult<IReadOnlyList<GitWorktree>>([new(Environment.CurrentDirectory, HeadHash, "main", false, false, false, false, true, true)]),
                "GetCurrentBranchRemoteStatusAsync" => Task.FromResult(new GitCurrentBranchRemoteStatus(false, null, null)),
                "GetLastCommitAsync" => Task.FromResult(new GitCommit("hash", "hash", "user", "", null, "title", "message")),
                "GetOperationStateAsync" => Task.FromResult(GitOperationState.None),
                "GetStatusAsync" => FailStatus ? Task.FromException<GitStatusSnapshot>(new DirectoryNotFoundException())
                    : Task.FromResult(new GitStatusSnapshot([], IncludeSecondFile
                        ? [new("first.txt", "Modified", state: GitChangeState.Unstaged), new("second.txt", "Modified", state: GitChangeState.Unstaged)]
                        : [new("first.txt", "Modified", state: GitChangeState.Unstaged)], [])),
                "GetRemotesAsync" => Task.FromResult<IReadOnlyList<GitRemote>>([new("origin", "url", "url")]),
                "GetLocalBranchesAsync" => Task.FromResult<IReadOnlyList<GitBranch>>([new("feature/main", true, false, "hash", "message", null)]),
                "GetRemoteBranchesAsync" => Task.FromResult<IReadOnlyList<GitBranch>>([]),
                _ => throw new NotSupportedException(method)
            });
            ISettingsService settings = Stub.Create<ISettingsService>((_, _) => new AppSettings());
            IAsyncCommandExecutor executor = Stub.Create<IAsyncCommandExecutor>((_, args) => ((Func<Task>)args![0]!)());
            IClipboardService clipboard = Stub.Create<IClipboardService>();
            MainWindowViewModel window = new(Stub.Create<IRecentRepositoriesService>((_, _) => Array.Empty<RepositoryInfo>()),
                strings, git, clipboard, new TestProductInfoService(), messenger, settings);
            window.SetCurrentRepository(new(Environment.CurrentDirectory, "repo", "main"), []);
            ConflictEditorViewModel conflict = new(null!, git, strings, messenger, executor);
            Repository = new(Stub.Create<IStoragePickerService>(), Stub.Create<IRecentRepositoriesService>(), git, strings, clipboard,
                Stub.Create<IFileExplorerService>(), Stub.Create<IDialogService>(), window, messenger, executor,
                new TestExecutionContextService(new InMemoryRepositoryFileSystem()),
                Stub.Create<IExecutionRepositoryDiscoveryService>((_, _) => Task.FromResult<RepositoryInfo?>(
                    new RepositoryInfo(Environment.CurrentDirectory, "repo", "main"))), Stub.Create<IApplicationInstanceLauncher>());
            Changes = new(window, git, strings, clipboard, Stub.Create<IDialogService>(), settings, null!, conflict, messenger, executor);
            Branches = new(window, null!, git, strings, clipboard, Stub.Create<IDialogService>(), messenger, executor,
                new TestExecutionContextService(new InMemoryRepositoryFileSystem()));
        }
    }
}
