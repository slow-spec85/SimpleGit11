using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Execution;
using SimpleGit11.Services.Git;
using SimpleGit11.Tests.TestInfrastructure;
using SimpleGit11.ViewModels;
using Stub = SimpleGit11.Tests.ViewModels.SettingsPullSettingsTests.ServiceStub;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class ReferenceTreeTests
{
    [TestMethod]
    public void ToggleFolderExpansion_OpensAndClosesFolderAndPreservesStateAfterFiltering()
    {
        BranchesViewModel model = CreateModel();
        model.Branches.Add(new("main", true, false, "hash", "", null));
        model.Branches.Add(new("feature/a", false, false, "hash", "", null));
        model.SearchText = " ";
        ReferenceTreeItemViewModel folder = model.BranchTree.First(item => item.IsFolder);
        Assert.IsFalse(folder.IsExpanded);

        folder.ToggleFolderExpansionCommand.Execute(null);
        Assert.IsTrue(folder.IsExpanded);
        model.SearchText = "  ";
        folder = model.BranchTree.First(item => item.IsFolder);
        Assert.IsTrue(folder.IsExpanded);
        folder.ToggleFolderExpansionCommand.Execute(null);
        Assert.IsFalse(folder.IsExpanded);
        model.SearchText = "";
        Assert.IsFalse(model.BranchTree.First(item => item.IsFolder).IsExpanded);
    }

    [TestMethod]
    public void ToggleFolderExpansion_DoesNotExpandBranchesOrTags()
    {
        ReferenceTreeItemViewModel branch = new("main", "main", branch: new GitBranch("main", true, false, "hash", "", null));
        ReferenceTreeItemViewModel tag = new("v1", "v1", tag: new GitTag("v1", false, false, "hash", "", null));
        branch.ToggleFolderExpansionCommand.Execute(null);
        tag.ToggleFolderExpansionCommand.Execute(null);
        Assert.IsFalse(branch.IsExpanded);
        Assert.IsFalse(tag.IsExpanded);
    }

    [TestMethod]
    public void Branches_GroupNestedNamesAndAggregateLatestKnownDate()
    {
        BranchesViewModel model = CreateModel();
        DateTime older = new(2026, 10, 1, 12, 0, 0);
        DateTime newest = older.AddDays(5);
        GitBranch branch = new("feature/auth/login", false, false, "abcdef12", "Login added", newest);
        model.Branches.Add(branch);
        model.Branches.Add(new("feature/old", false, false, "12345678", "Old change", older));
        model.Branches.Add(new("feature/unknown", false, false, "12345678", "No date", null));
        model.Branches.Add(new("main", true, false, "12345678", "Main change", older));
        model.SearchText = " ";

        Assert.HasCount(2, model.BranchTree);
        ReferenceTreeItemViewModel feature = model.BranchTree[0];
        Assert.AreEqual("feature", feature.Name);
        Assert.IsTrue(feature.IsFolder);
        Assert.AreEqual(newest, feature.LatestDate);
        Assert.AreEqual(newest.ToString("g"), feature.Metadata);
        ReferenceTreeItemViewModel auth = feature.Children[0];
        Assert.AreEqual("auth", auth.Name);
        Assert.AreEqual(newest, auth.LatestDate);
        ReferenceTreeItemViewModel login = auth.Children.Single();
        Assert.AreSame(branch, login.Branch);
        Assert.AreEqual("login", login.Name);
        Assert.AreEqual(branch.Name, login.FullName);
        Assert.AreEqual($"{branch.CommitMetadata}   {branch.LastCommitMessage}", login.Metadata);
        Assert.AreSame(model.SelectedBranch, model.SelectedBranchTreeItem!.Branch);
    }

    [TestMethod]
    public void Search_PrunesEmptyFoldersAndKeepsDateFromAllDescendants()
    {
        BranchesViewModel model = CreateModel();
        DateTime older = new(2026, 10, 1);
        DateTime newest = older.AddDays(8);
        model.Branches.Add(new("feature/old", false, false, "oldhash", "Find this", older));
        model.Branches.Add(new("feature/new", false, false, "newhash", "Other message", newest));
        model.Branches.Add(new("fix/hidden", false, false, "fixhash", "Other message", newest));
        model.SearchText = "Find this";

        ReferenceTreeItemViewModel feature = model.BranchTree.Single();
        Assert.AreEqual("feature", feature.Name);
        Assert.IsTrue(feature.IsExpanded);
        Assert.AreEqual(newest, feature.LatestDate);
        Assert.AreEqual("old", feature.Children.Single().Name);
        Assert.HasCount(1, model.FilteredBranches);
        model.SearchText = "absent";
        Assert.IsEmpty(model.BranchTree);
        Assert.IsNull(model.SelectedBranchTreeItem);
    }

    [TestMethod]
    public void FolderExpansion_IsRestoredAfterSearchAndScopeChanges()
    {
        BranchesViewModel model = CreateModel();
        model.Branches.Add(new("feature/a", true, false, "hash", "Change", null));
        model.RemoteBranches.Add(new("origin/feature/a", false, true, "hash", "Change", null));
        model.SearchText = " ";
        model.BranchTree.Single().IsExpanded = false;
        model.SearchText = "feature";
        Assert.IsTrue(model.BranchTree.Single().IsExpanded);
        model.SearchText = "";
        Assert.IsFalse(model.BranchTree.Single().IsExpanded);
        model.BranchTree.Single().IsExpanded = true;
        model.BranchScope = BranchListScope.Remote;
        Assert.AreEqual("origin", model.BranchTree.Single().Name);
        model.BranchScope = BranchListScope.Local;
        Assert.IsTrue(model.BranchTree.Single().IsExpanded);
    }

    [TestMethod]
    public void Tags_SupportNestedFoldersAndCombinedSecondLine()
    {
        BranchesViewModel model = CreateModel();
        DateTime date = new(2026, 10, 12);
        GitTag tag = new("release/v1/beta", false, true, "abcdef123456", "Beta release", date);
        model.Tags.Add(tag);
        model.Tags.Add(new("release/v2", false, false, "otherhash", "Next release", date.AddDays(1)));
        model.SearchText = " ";

        ReferenceTreeItemViewModel release = model.TagTree.Single();
        Assert.AreEqual(date.AddDays(1), release.LatestDate);
        ReferenceTreeItemViewModel beta = release.Children[0].Children.Single();
        Assert.AreSame(tag, beta.Tag);
        Assert.AreEqual("beta", beta.Name);
        Assert.AreEqual(tag.Name, beta.FullName);
        Assert.AreEqual($"{tag.CommitMetadata}   {tag.Subject}", beta.Metadata);
        model.SearchText = "Beta release";
        Assert.HasCount(1, model.FilteredTags);
        Assert.AreEqual(date.AddDays(1), model.TagTree.Single().LatestDate);
        Assert.IsTrue(model.TagTree.Single().IsExpanded);
    }

    [TestMethod]
    public void RemoteTags_GroupRemoteAndTagPathWithoutChangingReference()
    {
        BranchesViewModel model = CreateModel();
        GitTag tag = new("origin/release/v1", true, false, "hash", "", null, "origin", "release/v1");
        model.RemoteTags.Add(tag);
        model.BranchScope = BranchListScope.Remote;

        ReferenceTreeItemViewModel origin = model.TagTree.Single();
        Assert.AreEqual("origin", origin.Name);
        Assert.AreEqual("", origin.Metadata);
        ReferenceTreeItemViewModel leaf = origin.Children.Single().Children.Single();
        Assert.AreSame(tag, leaf.Tag);
        Assert.AreEqual("release/v1", leaf.Tag!.RemoteTagName);
        Assert.AreEqual("origin/release/v1", leaf.FullName);
    }

    [TestMethod]
    public void Names_PreserveCaseAndAllowLeafAndFolderWithSameDisplayName()
    {
        BranchesViewModel model = CreateModel();
        model.Branches.Add(new("origin/feature/a", false, false, "hash", "", null));
        model.Branches.Add(new("Origin/b", false, false, "hash", "", null));
        model.RemoteBranches.Add(new("origin/feature", false, true, "hash", "", null));
        model.BranchScope = BranchListScope.All;

        Assert.HasCount(2, model.BranchTree);
        Assert.AreEqual("Origin", model.BranchTree[0].Name);
        Assert.AreEqual("origin", model.BranchTree[1].Name);
        Assert.HasCount(2, model.BranchTree[1].Children);
        Assert.IsTrue(model.BranchTree[1].Children[0].IsFolder);
        Assert.IsFalse(model.BranchTree[1].Children[1].IsFolder);
        Assert.AreEqual("feature", model.BranchTree[1].Children[1].Name);
    }

    [TestMethod]
    public void SelectingReference_RevealsAllAncestorsAndKeepsGitReference()
    {
        BranchesViewModel model = CreateModel();
        model.Branches.Add(new("main", true, false, "hash", "", null));
        GitBranch branch = new("feature/auth/login", false, false, "hash", "", null);
        model.Branches.Add(branch);
        model.SearchText = " ";
        ReferenceTreeItemViewModel feature = model.BranchTree.First(item => item.Name == "feature");
        Assert.IsFalse(feature.IsExpanded);
        Assert.IsFalse(feature.Children.Single().IsExpanded);

        model.SelectedBranch = branch;

        Assert.IsTrue(feature.IsExpanded);
        Assert.IsTrue(feature.Children.Single().IsExpanded);
        Assert.AreSame(branch, model.SelectedBranchTreeItem!.Branch);
        Assert.AreEqual("feature/auth/login", model.SelectedBranch.Name);
    }

    [TestMethod]
    public void RepositoryChange_ClearsTreesAndKeepsExpansionScopedToRepository()
    {
        BranchesViewModel model = CreateModel(out MainWindowViewModel window);
        RepositoryInfo first = new("C:/first", "first", "main");
        RepositoryInfo second = new("C:/second", "second", "main");
        window.SetCurrentRepository(first, []);
        model.Branches.Add(new("main", true, false, "hash", "", null));
        model.Branches.Add(new("feature/a", false, false, "hash", "", null));
        model.Tags.Add(new("release/v1", false, false, "hash", "", null));
        model.SearchText = " ";
        model.BranchTree.First(item => item.IsFolder).IsExpanded = true;
        window.SetCurrentRepository(second, []);
        Assert.IsEmpty(model.BranchTree);
        Assert.IsEmpty(model.TagTree);
        Assert.IsNull(model.SelectedBranchTreeItem);
        Assert.IsNull(model.SelectedTagTreeItem);

        model.Branches.Add(new("main", true, false, "hash", "", null));
        model.Branches.Add(new("feature/a", false, false, "hash", "", null));
        model.SearchText = "  ";
        Assert.IsFalse(model.BranchTree.First(item => item.IsFolder).IsExpanded);
        window.SetCurrentRepository(first, []);
        model.Branches.Add(new("main", true, false, "hash", "", null));
        model.Branches.Add(new("feature/a", false, false, "hash", "", null));
        model.SearchText = " ";
        Assert.IsTrue(model.BranchTree.First(item => item.IsFolder).IsExpanded);
    }

    private static BranchesViewModel CreateModel() => CreateModel(out _);

    [TestMethod]
    public async Task SelectReference_TagThenMainClearsOldTreeSelectionBeforeReplacingNodes()
    {
        BranchesViewModel model = CreateModel();
        GitBranch main = new("main", true, false, "hash", "", null);
        GitTag tag = new("release/v1", false, false, "hash", "", null);
        model.Branches.Add(main);
        model.Tags.Add(tag);
        model.SearchText = " ";
        IReadOnlyList<ReferenceTreeItemViewModel> displayedBranches = model.BranchTree;
        IReadOnlyList<ReferenceTreeItemViewModel> displayedTags = model.TagTree;
        ReferenceTreeItemViewModel? displayedBranchSelection = model.SelectedBranchTreeItem;
        ReferenceTreeItemViewModel? displayedTagSelection = model.SelectedTagTreeItem;
        int branchReplacements = 0;
        int tagReplacements = 0;
        model.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(model.SelectedBranchTreeItem):
                    displayedBranchSelection = model.SelectedBranchTreeItem;
                    Assert.IsTrue(displayedBranchSelection is null || Contains(displayedBranches, displayedBranchSelection),
                        "A selected branch node must belong to the tree currently displayed by the control.");
                    break;
                case nameof(model.BranchTree):
                    Assert.IsNull(displayedBranchSelection, "The old selection must be cleared before replacing branch nodes.");
                    displayedBranches = model.BranchTree;
                    branchReplacements++;
                    break;
                case nameof(model.SelectedTagTreeItem):
                    displayedTagSelection = model.SelectedTagTreeItem;
                    Assert.IsTrue(displayedTagSelection is null || Contains(displayedTags, displayedTagSelection));
                    break;
                case nameof(model.TagTree):
                    Assert.IsNull(displayedTagSelection, "The old selection must be cleared before replacing tag nodes.");
                    displayedTags = model.TagTree;
                    tagReplacements++;
                    break;
            }
        };

        for (int visit = 0; visit < 3; visit++)
        {
            await model.SelectReferenceAsync(new(tag.Name, GitCommitReferenceKind.Tag));
            Assert.AreSame(tag, model.SelectedTag);
            // Returning to the cached page refreshes the reference trees before navigation selects main.
            model.SearchText = " ";
            model.SearchText = "";
            await model.SelectReferenceAsync(new(main.Name, GitCommitReferenceKind.LocalBranch));
            Assert.AreSame(main, model.SelectedBranch);
            Assert.AreSame(model.SelectedBranchTreeItem, displayedBranchSelection);
            Assert.AreSame(model.SelectedTagTreeItem, displayedTagSelection);
        }
        Assert.IsTrue(branchReplacements >= 3);
        Assert.IsTrue(tagReplacements >= 3);

        static bool Contains(IReadOnlyList<ReferenceTreeItemViewModel> tree, ReferenceTreeItemViewModel selected)
        {
            return tree.Any(item => ReferenceEquals(item, selected) || Contains(item.Children, selected));
        }
    }

    [TestMethod]
    public async Task SelectReference_LocalBranchClearsSearchAndRevealsExactNestedName()
    {
        BranchesViewModel model = CreateModel();
        GitBranch target = new("feature/auth/login", false, false, "hash", "", null);
        model.Branches.Add(new("fix/login", true, false, "hash", "", null));
        model.Branches.Add(target);
        model.RemoteBranches.Add(new("origin/feature/auth/login", false, true, "hash", "", null));
        model.BranchScope = BranchListScope.Remote;
        model.ReferenceKind = ReferenceListKind.Tags;
        model.SearchText = "hidden";

        await model.SelectReferenceAsync(new(target.Name, GitCommitReferenceKind.LocalBranch));

        Assert.AreEqual("", model.SearchText);
        Assert.AreEqual(ReferenceListKind.Branches, model.ReferenceKind);
        Assert.AreEqual(BranchListScope.Local, model.BranchScope);
        Assert.AreSame(target, model.SelectedBranch);
        Assert.AreSame(target, model.SelectedBranchTreeItem!.Branch);
        Assert.IsTrue(model.SelectedBranchTreeItem.Parent!.IsExpanded);
        Assert.IsTrue(model.SelectedBranchTreeItem.Parent.Parent!.IsExpanded);
    }

    [TestMethod]
    public async Task SelectReference_RemoteBranchUsesFullNameAndRemoteScope()
    {
        BranchesViewModel model = CreateModel();
        GitBranch target = new("upstream/feature/main", false, true, "hash", "", null);
        model.Branches.Add(new("feature/main", true, false, "hash", "", null));
        model.RemoteBranches.Add(new("origin/feature/main", false, true, "hash", "", null));
        model.RemoteBranches.Add(target);
        model.SearchText = "hidden";

        await model.SelectReferenceAsync(new(target.Name, GitCommitReferenceKind.RemoteBranch));

        Assert.AreEqual(BranchListScope.Remote, model.BranchScope);
        Assert.AreSame(target, model.SelectedBranch);
        Assert.AreEqual(target.Name, model.SelectedBranchTreeItem!.FullName);
        Assert.IsTrue(model.SelectedBranchTreeItem.Parent!.IsExpanded);
        Assert.IsTrue(model.SelectedBranchTreeItem.Parent.Parent!.IsExpanded);
    }

    [TestMethod]
    public async Task SelectReference_RepeatedRemoteNavigationRevealsRebuiltAncestorPath()
    {
        BranchesViewModel model = CreateModel();
        model.Branches.Add(new("main", true, false, "hash", "", null));
        GitBranch origin = new("origin/feature/nested/main", false, true, "hash", "", null);
        GitBranch upstream = new("upstream/feature/nested/main", false, true, "hash", "", null);
        model.RemoteBranches.Add(origin);
        model.RemoteBranches.Add(upstream);

        for (int visit = 0; visit < 3; visit++)
        {
            foreach (GitBranch target in new[] { origin, upstream })
            {
                await model.SelectReferenceAsync(new("main", GitCommitReferenceKind.LocalBranch));
                model.SearchText = "hidden";
                await model.SelectReferenceAsync(new(target.Name, GitCommitReferenceKind.RemoteBranch));

                Assert.AreEqual(BranchListScope.Remote, model.BranchScope);
                Assert.AreSame(target, model.SelectedBranchTreeItem!.Branch);
                int ancestors = 0;
                for (ReferenceTreeItemViewModel? parent = model.SelectedBranchTreeItem.Parent;
                    parent is not null; parent = parent.Parent)
                {
                    Assert.IsTrue(parent.IsExpanded, $"Ancestor {parent.FullName} must be revealed on visit {visit}.");
                    ancestors++;
                }
                Assert.AreEqual(3, ancestors);
                model.SelectedBranchTreeItem.Parent!.IsExpanded = false;
            }
        }
    }

    [TestMethod]
    public async Task SelectReference_TagSwitchesModeAndSelectsLocalTagWithExactName()
    {
        BranchesViewModel model = CreateModel();
        GitTag target = new("release/v1", false, false, "hash", "", null);
        model.Tags.Add(new("other/v1", false, false, "hash", "", null));
        model.Tags.Add(target);
        model.RemoteTags.Add(new("origin/release/v1", true, false, "hash", "", null, "origin", "release/v1"));
        model.BranchScope = BranchListScope.Remote;
        model.SearchText = "hidden";

        await model.SelectReferenceAsync(new(target.Name, GitCommitReferenceKind.Tag));

        Assert.AreEqual(ReferenceListKind.Tags, model.ReferenceKind);
        Assert.AreEqual(BranchListScope.Local, model.BranchScope);
        Assert.AreSame(target, model.SelectedTag);
        Assert.AreSame(target, model.SelectedTagTreeItem!.Tag);
        Assert.IsTrue(model.SelectedTagTreeItem.Parent!.IsExpanded);
    }

    [TestMethod]
    public async Task SelectReference_RemovedReferenceDoesNotSelectAnotherItem()
    {
        BranchesViewModel model = CreateModel();
        model.Branches.Add(new("main", true, false, "hash", "", null));
        model.Tags.Add(new("v1", false, false, "hash", "", null));

        await model.SelectReferenceAsync(new("deleted", GitCommitReferenceKind.LocalBranch));
        Assert.IsNull(model.SelectedBranch);
        Assert.IsNull(model.SelectedBranchTreeItem);
        await model.SelectReferenceAsync(new("deleted", GitCommitReferenceKind.Tag));
        Assert.IsNull(model.SelectedTag);
        Assert.IsNull(model.SelectedTagTreeItem);
    }

    [TestMethod]
    public async Task SelectReference_TagWaitsForLazyLoadingBeforeSelecting()
    {
        TaskCompletionSource<IReadOnlyList<GitTag>> tagsLoaded = new();
        IGitTagService tags = Stub.Create<IGitTagService>((method, _) => method switch
        {
            "GetHeadCommitHashAsync" => Task.FromResult<string?>("hash"),
            _ => throw new NotSupportedException(method)
        });
        IGitService git = Stub.Create<IGitService>((method, args) => method switch
        {
            "ExecuteAsync" => ((Func<Task>)args![0]!)(),
            "GetLocalTagsAsync" => tagsLoaded.Task,
            "get_Tags" => tags,
            _ => throw new NotSupportedException(method)
        });
        BranchesViewModel model = CreateModel(out MainWindowViewModel window, git);
        window.SetCurrentRepository(new RepositoryInfo(Environment.CurrentDirectory, "repo", "main"), []);
        GitTag target = new("release/v1", false, false, "hash", "", null);

        Task selection = model.SelectReferenceAsync(new(target.Name, GitCommitReferenceKind.Tag));
        Assert.IsFalse(selection.IsCompleted);
        Assert.IsNull(model.SelectedTag);
        tagsLoaded.SetResult([new("other/v1", false, false, "hash", "", null), target]);
        await selection;

        Assert.AreEqual(target.Name, model.SelectedTag!.Name);
        Assert.AreSame(model.Tags.Single(tag => tag.Name == target.Name), model.SelectedTag);
        Assert.AreSame(model.SelectedTag, model.SelectedTagTreeItem!.Tag);
        Assert.IsTrue(model.SelectedTagTreeItem.Parent!.IsExpanded);
    }

    private static BranchesViewModel CreateModel(out MainWindowViewModel window, IGitService? gitService = null)
    {
        StrongReferenceMessenger messenger = new();
        ILocalizationService localization = Stub.Create<ILocalizationService>((_, args) => args![0]);
        IGitService git = gitService ?? Stub.Create<IGitService>();
        IClipboardService clipboard = Stub.Create<IClipboardService>();
        window = new MainWindowViewModel(
            Stub.Create<IRecentRepositoriesService>((_, _) => Array.Empty<RepositoryInfo>()),
            localization, git, clipboard, new TestProductInfoService(), messenger,
            Stub.Create<ISettingsService>((_, _) => new AppSettings()));
        return new BranchesViewModel(window, null!, git, localization, clipboard,
            Stub.Create<IDialogService>(), messenger, Stub.Create<IAsyncCommandExecutor>(),
            Stub.Create<IExecutionContextService>());
    }
}
