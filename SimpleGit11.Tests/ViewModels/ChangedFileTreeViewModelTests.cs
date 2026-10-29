using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Models;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class ChangedFileTreeViewModelTests
{
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
        Assert.AreEqual(Symbol.Folder, model.FolderViewSymbol);
        Assert.AreEqual(Visibility.Visible, model.FlatListVisibility);
        Assert.AreEqual(Visibility.Collapsed, model.TreeVisibility);

        model.ToggleFoldersCommand.Execute(null);
        Assert.IsTrue(model.ShowFolders);
        Assert.AreEqual(Symbol.Accept, model.FolderViewSymbol);
        Assert.AreEqual(Visibility.Collapsed, model.FlatListVisibility);
        Assert.AreEqual(Visibility.Visible, model.TreeVisibility);

        model.ToggleFoldersCommand.Execute(null);
        Assert.IsFalse(model.ShowFolders);
        Assert.AreEqual(Symbol.Folder, model.FolderViewSymbol);
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
        Assert.AreEqual(Symbol.Accept, model.FolderViewSymbol);
        Assert.IsFalse(model.Roots.Single().IsExpanded);
        Assert.IsFalse(model.Roots.Single().Children.Single().IsExpanded);
        Assert.AreSame(replacement, model.Roots.Single().Children.Single().Children.Single().File);
        model.Refresh([]);
        Assert.IsEmpty(model.Roots);
        Assert.IsTrue(model.ShowFolders);
        Assert.AreEqual(Symbol.Accept, model.FolderViewSymbol);
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
