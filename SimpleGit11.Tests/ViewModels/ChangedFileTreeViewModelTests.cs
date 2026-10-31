using Microsoft.UI.Xaml;
using SimpleGit11.Models;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class ChangedFileTreeViewModelTests
{
    [TestMethod]
    public void FolderMenu_ExpansionCommandsAreMutuallyExclusiveForFolderState()
    {
        ChangedFileTreeViewModel model = new();
        model.Refresh([new("src/nested/File.cs", "Modified")]);
        ChangedFileTreeItemViewModel folder = model.Roots.Single();
        int expansionNotifications = 0;
        int collapseNotifications = 0;
        model.ExpandAllFoldersCommand.CanExecuteChanged += (_, _) => expansionNotifications++;
        model.CollapseAllFoldersCommand.CanExecuteChanged += (_, _) => collapseNotifications++;
        Assert.IsFalse(model.ExpandAllFoldersCommand.CanExecute(folder));
        Assert.IsTrue(model.CollapseAllFoldersCommand.CanExecute(folder));
        model.CollapseAllFoldersCommand.Execute(folder);
        Assert.AreEqual(1, expansionNotifications);
        Assert.AreEqual(1, collapseNotifications);
        Assert.IsTrue(model.ExpandAllFoldersCommand.CanExecute(folder));
        Assert.IsFalse(model.CollapseAllFoldersCommand.CanExecute(folder));
        model.ExpandAllFoldersCommand.Execute(folder);
        Assert.IsFalse(model.ExpandAllFoldersCommand.CanExecute(folder));
        Assert.IsTrue(model.CollapseAllFoldersCommand.CanExecute(folder));
        ChangedFileTreeItemViewModel leaf = folder.Children.Single().Children.Single();
        Assert.IsFalse(model.ExpandAllFoldersCommand.CanExecute(leaf));
        Assert.IsFalse(model.CollapseAllFoldersCommand.CanExecute(leaf));
        Assert.IsFalse(model.ExpandAllFoldersCommand.CanExecute(null));
        Assert.IsFalse(model.CollapseAllFoldersCommand.CanExecute(null));
    }

    [TestMethod]
    public void FolderMenu_ExpandsAndCollapsesOnlyTheChosenSubtreeIncludingHiddenFolders()
    {
        ChangedFileTreeViewModel model = new();
        model.Refresh([new("src/nested/deep/File.cs", "Modified", new DiffStat(5, 2)), new("other/Keep.cs", "Modified")]);
        ChangedFileTreeItemViewModel folder = model.Roots.Single(item => item.Name == "src");
        ChangedFileTreeItemViewModel nested = folder.Children.Single();
        ChangedFileTreeItemViewModel deep = nested.Children.Single();
        ChangedFileTreeItemViewModel leaf = deep.Children.Single();
        ChangedFileTreeItemViewModel other = model.Roots.Single(item => item.Name == "other");
        other.IsExpanded = false;
        model.CollapseAllFoldersCommand.Execute(folder);
        Assert.IsFalse(folder.IsExpanded);
        Assert.IsFalse(nested.IsExpanded);
        Assert.IsFalse(deep.IsExpanded);
        Assert.IsTrue(leaf.IsExpanded);
        Assert.IsFalse(other.IsExpanded);
        Assert.IsTrue(folder.IsStatVisible);
        Assert.AreEqual(5, folder.Stat.AddedLines);
        Assert.IsFalse(model.VisibleItems.Contains(leaf));
        model.ExpandAllFoldersCommand.Execute(folder);
        Assert.IsFalse(other.IsExpanded);
        Assert.IsTrue(folder.IsExpanded);
        Assert.IsTrue(nested.IsExpanded);
        Assert.IsTrue(deep.IsExpanded);
        Assert.IsTrue(model.VisibleItems.Contains(leaf));
        Assert.IsFalse(folder.IsStatVisible);
        model.CollapseAllFoldersCommand.Execute(null);
        model.CollapseAllFoldersCommand.Execute(leaf);
        Assert.IsTrue(leaf.IsExpanded);
        Assert.IsTrue(folder.IsExpanded);
    }

    [TestMethod]
    public void FolderStats_IncludeAllDescendantsAndAppearOnlyWhenCollapsed()
    {
        ChangedFileTreeViewModel model = new();
        model.Refresh([new("src/A.cs", "Modified", new DiffStat(2, 3)),
            new("src/nested/B.cs", "Modified", new DiffStat(5, 7)),
            new("Other.cs", "Modified", new DiffStat(100, 200))]);
        ChangedFileTreeItemViewModel folder = model.Roots.Single(item => item.IsFolder);
        ChangedFileTreeItemViewModel nested = folder.Children.Single(item => item.IsFolder);
        Assert.AreEqual(7, folder.Stat.AddedLines);
        Assert.AreEqual(10, folder.Stat.RemovedLines);
        Assert.AreEqual(5, nested.Stat.AddedLines);
        Assert.IsFalse(folder.IsStatVisible);
        Assert.IsFalse(nested.IsStatVisible);
        Assert.IsTrue(folder.Children.Single(item => item.IsFile).IsStatVisible);
        List<string?> changed = [];
        folder.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        nested.IsExpanded = false;
        folder.IsExpanded = false;
        Assert.IsTrue(folder.IsStatVisible);
        Assert.IsTrue(nested.IsStatVisible);
        Assert.AreEqual(7, folder.Stat.AddedLines);
        Assert.Contains(nameof(ChangedFileTreeItemViewModel.IsStatVisible), changed);
        folder.IsExpanded = true;
        Assert.IsFalse(folder.IsStatVisible);
        Assert.IsTrue(nested.IsStatVisible);
    }

    [TestMethod]
    public void FolderStats_RefreshReusedCollapsedAncestorsAndNotifyBindings()
    {
        ChangedFileTreeViewModel model = new();
        GitChangedFile unchanged = new("src/Keep.cs", "Modified", new DiffStat(1, 2));
        model.Refresh([unchanged, new("src/nested/Old.cs", "Modified", new DiffStat(3, 4))]);
        ChangedFileTreeItemViewModel folder = model.Roots.Single();
        ChangedFileTreeItemViewModel nested = folder.Children.Single(item => item.IsFolder);
        folder.IsExpanded = false;
        List<string?> changed = [];
        folder.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        model.Refresh([unchanged, new("src/nested/New.cs", "Modified", new DiffStat(10, 20))]);
        Assert.AreSame(folder, model.Roots.Single());
        Assert.AreSame(nested, folder.Children.Single(item => item.IsFolder));
        Assert.IsTrue(folder.IsStatVisible);
        Assert.AreEqual(11, folder.Stat.AddedLines);
        Assert.AreEqual(22, folder.Stat.RemovedLines);
        Assert.AreEqual(10, nested.Stat.AddedLines);
        Assert.Contains(nameof(ChangedFileTreeItemViewModel.Stat), changed);
        model.Refresh([unchanged]);
        Assert.AreEqual(1, folder.Stat.AddedLines);
        Assert.AreEqual(2, folder.Stat.RemovedLines);
    }

    [TestMethod]
    public void FolderStats_CountStagedAndUnstagedEntriesAndKeepCaseSensitiveFoldersSeparate()
    {
        ChangedFileTreeViewModel model = new();
        model.Refresh([new("src/File.cs", "Modified", new DiffStat(2, 3), GitChangeState.Staged),
            new("src/File.cs", "Modified", new DiffStat(5, 7), GitChangeState.Unstaged),
            new("Src/File.cs", "Modified", new DiffStat(100, 200)), new("empty/Binary.dat", "Modified")]);
        Assert.AreEqual(7, model.Roots.Single(item => item.Name == "src").Stat.AddedLines);
        Assert.AreEqual(10, model.Roots.Single(item => item.Name == "src").Stat.RemovedLines);
        Assert.AreEqual(100, model.Roots.Single(item => item.Name == "Src").Stat.AddedLines);
        Assert.AreEqual(0, model.Roots.Single(item => item.Name == "empty").Stat.AddedLines);
    }

    [TestMethod]
    public void Refresh_PreservesUnchangedNodesWhenAnotherFileChanges()
    {
        ChangedFileTreeViewModel model = new();
        GitChangedFile unchanged = new("src/Keep.cs", "Modified");
        model.Refresh([unchanged, new("src/Old.cs", "Modified")]);
        ChangedFileTreeItemViewModel folder = model.Roots.Single();
        ChangedFileTreeItemViewModel leaf = folder.Children.Single(item => ReferenceEquals(item.File, unchanged));
        folder.IsExpanded = false;

        model.Refresh([unchanged, new("src/New.cs", "Added")]);

        Assert.AreSame(folder, model.Roots.Single());
        Assert.AreSame(leaf, folder.Children.Single(item => ReferenceEquals(item.File, unchanged)));
        Assert.IsFalse(folder.IsExpanded);
        Assert.AreSame(folder, folder.Children.Single(item => item.File?.Path == "src/New.cs").Parent);
    }

    [TestMethod]
    public void ViewMode_DefaultsToFlatAndCanBeToggledBack()
    {
        ChangedFileTreeViewModel model = new();
        Assert.IsFalse(model.ShowFolders);
        Assert.AreEqual(Visibility.Visible, model.FlatListVisibility);
        Assert.AreEqual(Visibility.Collapsed, model.TreeVisibility);

        model.ShowFolders = !model.ShowFolders;
        Assert.IsTrue(model.ShowFolders);
        Assert.AreEqual(Visibility.Collapsed, model.FlatListVisibility);
        Assert.AreEqual(Visibility.Visible, model.TreeVisibility);

        model.ShowFolders = !model.ShowFolders;
        Assert.IsFalse(model.ShowFolders);
    }

    [TestMethod]
    public void Refresh_BuildsNestedFoldersAndSortsFoldersBeforeFiles()
    {
        ChangedFileTreeViewModel model = new();
        GitChangedFile nested = new("src/Views/Page.xaml", "Added", new DiffStat(3, 1));
        GitChangedFile root = new("README.md", "Modified");
        model.Refresh([root, new("src/z.cs", "Modified"), nested, new("docs/Help.md", "Added"), new("src/A.cs", "Modified")]);

        CollectionAssert.AreEqual(new[] { "docs", "src", "README.md" }, model.Roots.Select(item => item.Name).ToArray());
        ChangedFileTreeItemViewModel source = model.Roots[1];
        CollectionAssert.AreEqual(new[] { "Views", "A.cs", "z.cs" }, source.Children.Select(item => item.Name).ToArray());
        ChangedFileTreeItemViewModel leaf = source.Children[0].Children.Single();
        Assert.AreEqual("src/Views", source.Children[0].Path);
        Assert.AreSame(nested, leaf.File);
        Assert.AreSame(nested.Stat, leaf.Stat);
        Assert.AreEqual("A", leaf.StatusIndicator);
        Assert.AreSame(root, model.Roots[2].File);
    }

    [TestMethod]
    public void Refresh_PreservesDuplicateStatesAndCaseSensitiveGitPaths()
    {
        ChangedFileTreeViewModel model = new();
        GitChangedFile staged = new("src/File.cs", "Modified", state: GitChangeState.Staged);
        GitChangedFile unstaged = new("src/File.cs", "Modified", state: GitChangeState.Unstaged);
        GitChangedFile differentCase = new("Src/File.cs", "Conflict", state: GitChangeState.Conflicted);
        model.Refresh([staged, unstaged, differentCase]);

        Assert.HasCount(2, model.Roots);
        ChangedFileTreeItemViewModel source = model.Roots.Single(item => item.Name == "src");
        CollectionAssert.AreEqual(new[] { staged, unstaged }, source.Children.Select(item => item.File).ToArray());
        Assert.IsTrue(source.Children[0].IsStaged);
        Assert.IsFalse(source.Children[1].IsStaged);
        Assert.IsTrue(model.Roots.Single(item => item.Name == "Src").Children.Single().IsConflicted);
    }

    [TestMethod]
    public void Refresh_NormalizesSeparatorsAndRetainsExpansionForRemainingDirectories()
    {
        ChangedFileTreeViewModel model = new();
        model.ShowFolders = true;
        model.Refresh([new("src\\nested\\Old.cs", "Modified"), new("docs/Help.md", "Added")]);
        ChangedFileTreeItemViewModel source = model.Roots.Single(item => item.Name == "src");
        source.IsExpanded = false;
        source.Children.Single().IsExpanded = false;

        GitChangedFile replacement = new("src/nested/New.cs", "Modified");
        model.Refresh([replacement]);

        Assert.IsTrue(model.ShowFolders);
        Assert.IsFalse(model.Roots.Single().IsExpanded);
        Assert.IsFalse(model.Roots.Single().Children.Single().IsExpanded);
        Assert.AreSame(replacement, model.Roots.Single().Children.Single().Children.Single().File);
        model.Refresh([]);
        Assert.IsEmpty(model.Roots);
        Assert.IsTrue(model.ShowFolders);
    }
    [TestMethod]
    public void VisibleItems_FollowDirectoryDepthAndRetainParentLinks()
    {
        ChangedFileTreeViewModel model = new();
        GitChangedFile nested = new("src/nested/File.cs", "Modified");
        model.Refresh([new("README.md", "Added"), nested]);

        CollectionAssert.AreEqual(new[] { "src", "src/nested", "src/nested/File.cs", "README.md" },
            model.VisibleItems.Select(item => item.Path).ToArray());
        ChangedFileTreeItemViewModel leaf = model.VisibleItems[2];
        Assert.AreEqual(2, leaf.Depth);
        Assert.AreEqual(32.0, leaf.Indentation.Left);
        Assert.AreSame(model.VisibleItems[1], leaf.Parent);
        Assert.AreSame(model.VisibleItems[0], leaf.Parent!.Parent);
        Assert.IsNull(model.VisibleItems[3].Parent);
        Assert.AreSame(nested, leaf.File);
    }

    [TestMethod]
    public void CollapsingAndExpandingFolder_ChangesOnlyDescendantsAndKeepsOtherRows()
    {
        ChangedFileTreeViewModel model = new();
        model.Refresh([new("src/nested/File.cs", "Modified"), new("Other.cs", "Modified")]);
        ChangedFileTreeItemViewModel source = model.Roots[0];
        ChangedFileTreeItemViewModel other = model.VisibleItems.Last();
        ChangedFileTreeItemViewModel leaf = model.VisibleItems[2];
        object collection = model.VisibleItems;
        List<System.Collections.Specialized.NotifyCollectionChangedAction> actions = [];
        model.VisibleItems.CollectionChanged += (_, args) => actions.Add(args.Action);
        string expandedGlyph = source.ExpansionGlyph;

        source.IsExpanded = false;

        CollectionAssert.AreEqual(new[] { source, other }, model.VisibleItems.ToArray());
        Assert.AreNotEqual(expandedGlyph, source.ExpansionGlyph);
        Assert.IsTrue(actions.All(action => action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove));

        actions.Clear();
        source.IsExpanded = true;

        Assert.AreSame(collection, model.VisibleItems);
        Assert.AreSame(other, model.VisibleItems.Last());
        Assert.AreSame(leaf, model.VisibleItems[2]);
        Assert.AreEqual(expandedGlyph, source.ExpansionGlyph);
        Assert.IsTrue(actions.All(action => action == System.Collections.Specialized.NotifyCollectionChangedAction.Add));
    }

    [TestMethod]
    public void Refresh_PreservesHiddenDescendantExpansionAndClearsVisibleRows()
    {
        ChangedFileTreeViewModel model = new();
        model.Refresh([new("src/nested/File.cs", "Modified")]);
        model.Roots[0].Children[0].IsExpanded = false;
        model.Roots[0].IsExpanded = false;

        model.Refresh([new("src/nested/New.cs", "Modified")]);

        CollectionAssert.AreEqual(new[] { "src" }, model.VisibleItems.Select(item => item.Path).ToArray());
        model.Roots[0].IsExpanded = true;
        CollectionAssert.AreEqual(new[] { "src", "src/nested" }, model.VisibleItems.Select(item => item.Path).ToArray());
        model.Roots[0].Children[0].IsExpanded = true;
        Assert.AreEqual("src/nested/New.cs", model.VisibleItems.Last().Path);
        model.Refresh([]);
        Assert.IsEmpty(model.VisibleItems);
    }
    [TestMethod]
    public void FolderExpansionCommand_TogglesVisibleChildrenAndDoesNotToggleFiles()
    {
        ChangedFileTreeViewModel model = new();
        model.Refresh([new("src/File.cs", "Modified")]);
        ChangedFileTreeItemViewModel folder = model.Roots.Single();
        ChangedFileTreeItemViewModel file = folder.Children.Single();

        folder.ToggleFolderExpansionCommand.Execute(null);
        CollectionAssert.AreEqual(new[] { folder }, model.VisibleItems.ToArray());
        Assert.IsFalse(folder.IsExpanded);

        folder.ToggleFolderExpansionCommand.Execute(null);
        CollectionAssert.AreEqual(new[] { folder, file }, model.VisibleItems.ToArray());
        Assert.IsTrue(folder.IsExpanded);

        file.ToggleFolderExpansionCommand.Execute(null);
        Assert.IsTrue(file.IsExpanded);
        CollectionAssert.AreEqual(new[] { folder, file }, model.VisibleItems.ToArray());
    }
}
