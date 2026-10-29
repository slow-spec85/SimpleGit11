using System.Reflection;
using System.Xml.Linq;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Controls;
using SimpleGit11.Models;
using SimpleGit11.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SimpleGit11.Tests.Architecture;

[TestClass]
public sealed class ChangedFileStatsXamlTests
{
    [TestMethod]
    [DataRow("Controls", "CommitBrowserView.xaml", "HistoryFileContextMenu")]
    [DataRow("Pages", "ChangesPage.xaml", "ChangesFileContextMenu")]
    public void FileMenus_UseManualOpeningSoContextRequestedCanSupplyCommandParameters(
        string folder, string file, string menuName)
    {
        XDocument document = LoadApplicationXaml(folder, file);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement menu = document.Descendants().Single(element =>
            (string?)element.Attribute(xaml + "Name") == menuName);
        // ContextFlyout suppresses ContextRequested, leaving file-dependent commands disabled.
        Assert.AreEqual("FlyoutBase.AttachedFlyout", menu.Parent!.Name.LocalName);
        XElement list = menu.Ancestors().First(element => element.Name.LocalName == "ListView");
        Assert.IsNull(list.Attribute("ContextFlyout"));
        Assert.IsFalse(list.Elements().Any(element => element.Name.LocalName == "ListView.ContextFlyout"));
        Assert.AreEqual("ChangedFiles_ContextRequested", RequiredAttribute(list, "ContextRequested"));
        Assert.AreEqual("ChangedFiles_ContextCanceled", RequiredAttribute(list, "ContextCanceled"));
    }

    [TestMethod]
    public void FileTree_SingleSelectionDoesNotModifyTheNativeSelectedItemsVector()
    {
        GitChangedFile staged = new("src/File.cs", "Modified", state: GitChangeState.Staged);
        GitChangedFile unstaged = new("src/File.cs", "Modified", state: GitChangeState.Unstaged);
        ChangedFileTreeViewModel model = new();
        model.Refresh([staged, unstaged]);
        // Like WinUI in Single mode, this vector can be read but cannot be modified.
        IList<object> nativeSelection = Array.AsReadOnly<object>([new object()]);
        object? selectedItem = null;

        RestoreSelection(nativeSelection, model.VisibleItems, [unstaged],
            ListViewSelectionMode.Single, item => selectedItem = item);

        Assert.AreSame(unstaged, ((ChangedFileTreeItemViewModel)selectedItem!).File);
        Assert.HasCount(1, nativeSelection);

        model.Roots.Single().IsExpanded = false;
        RestoreSelection(nativeSelection, model.VisibleItems, [unstaged],
            ListViewSelectionMode.Single, item => selectedItem = item);
        Assert.IsNull(selectedItem);
    }

    [TestMethod]
    public void FileTree_SingleSelectionCanBeClearedWithoutANativeSelectedItemsVector()
    {
        GitChangedFile file = new("File.cs", "Modified");
        ChangedFileTreeViewModel model = new();
        model.Refresh([file]);
        object? selectedItem = null;
        RestoreSelection(null, model.VisibleItems, [file],
            ListViewSelectionMode.Single, item => selectedItem = item);
        Assert.AreSame(file, ((ChangedFileTreeItemViewModel)selectedItem!).File);
        RestoreSelection(null, model.VisibleItems, [],
            ListViewSelectionMode.Single, item => selectedItem = item);
        Assert.IsNull(selectedItem);
    }

    [TestMethod]
    public void FileTree_SelectionDisabledDoesNotWriteToEitherSelectionProperty()
    {
        GitChangedFile file = new("File.cs", "Modified");
        ChangedFileTreeViewModel model = new();
        model.Refresh([file]);
        IList<object> nativeSelection = Array.AsReadOnly<object>([new object()]);
        RestoreSelection(nativeSelection, model.VisibleItems, [file], ListViewSelectionMode.None);
        Assert.HasCount(1, nativeSelection);
    }

    private static void RestoreSelection(
        IList<object>? selectedItems,
        IEnumerable<ChangedFileTreeItemViewModel> visibleItems,
        IReadOnlyCollection<GitChangedFile> selectedFiles,
        ListViewSelectionMode selectionMode = ListViewSelectionMode.Extended,
        Action<object?>? setSelectedItem = null)
    {
        MethodInfo restoreSelection = typeof(ChangedFilesTree).GetMethod(
            "UpdateSelectedItems", BindingFlags.NonPublic | BindingFlags.Static)!;
        restoreSelection.Invoke(null, [selectedItems, visibleItems, selectedFiles, selectionMode,
            setSelectedItem ?? (Action<object?>)(_ => Assert.Fail("Only Single mode may set SelectedItem."))]);
    }

    [TestMethod]
    [DataRow("Controls", "CommitBrowserView.xaml", "HistoryDiffStatColumnMetrics")]
    [DataRow("Pages", "ChangesPage.xaml", "ChangesDiffStatColumnMetrics")]
    public void ChangedFileStats_UseSharedPresenterMetrics(string folder, string file, string metricsKey)
    {
        XDocument document = LoadApplicationXaml(folder, file);
        XElement metrics = document.Descendants().Single(element =>
            element.Name.LocalName == "DiffStatColumnMetrics");
        Assert.AreEqual(metricsKey, RequiredXamlAttribute(metrics, "Key"));

        XElement[] presenters = document.Descendants()
            .Where(element => element.Name.LocalName == "DiffStatPresenter")
            .ToArray();
        Assert.AreEqual(2, presenters.Length);

        XElement headerPresenter = presenters.Single(element =>
            (string?)element.Attribute("IsColumnWidthSource") == "True");
        XElement rowPresenter = presenters.Single(element =>
            element.Attribute("IsColumnWidthSource") is null);

        AssertUsesSharedMetrics(headerPresenter, metricsKey);
        AssertUsesSharedMetrics(rowPresenter, metricsKey);
        XElement tree = document.Descendants().Single(element => element.Name.LocalName == "ChangedFilesTree");
        Assert.AreEqual("{StaticResource " + metricsKey + "}", RequiredAttribute(tree, "ColumnMetrics"));
        StringAssert.Contains(RequiredAttribute(tree, "ContentPadding"), "GetContentPadding(ChangedFilesHeader.Padding");
        string flatListName = file == "CommitBrowserView.xaml" ? "HistoryChangedFilesListView" : "ChangedFilesListView";
        Assert.AreEqual("{x:Bind " + flatListName + ".Padding, Mode=OneWay}", RequiredAttribute(tree, "Padding"));
        StringAssert.Contains(
            RequiredAttribute(headerPresenter, "AddedText"),
            "ViewModel.ChangedFilesStat.AddedText");
        StringAssert.Contains(
            RequiredAttribute(headerPresenter, "RemovedText"),
            "ViewModel.ChangedFilesStat.RemovedText");
        StringAssert.Contains(RequiredAttribute(rowPresenter, "AddedText"), "Stat.AddedText");
        StringAssert.Contains(RequiredAttribute(rowPresenter, "RemovedText"), "Stat.RemovedText");

        Assert.IsFalse(document.Descendants().Attributes().Any(attribute =>
            attribute.Value.Contains("HistoryAddedLinesHeaderTextBlock", StringComparison.Ordinal)
            || attribute.Value.Contains("HistoryRemovedLinesHeaderTextBlock", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void DiffStatPresenter_MeasuresTwoAutoSizedTextColumns()
    {
        XDocument document = LoadApplicationXaml("Controls", "DiffStatPresenter.xaml");
        XElement[] columns = document.Descendants()
            .Where(element => element.Name.LocalName == "ColumnDefinition")
            .ToArray();
        CollectionAssert.AreEqual(
            new[] { "AddedColumn", "RemovedColumn" },
            columns.Select(element => RequiredXamlAttribute(element, "Name")).ToArray());
        CollectionAssert.AreEqual(
            new[] { "Auto", "Auto" },
            columns.Select(element => RequiredAttribute(element, "Width")).ToArray());

        XElement[] textBlocks = document.Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .ToArray();
        Assert.AreEqual(2, textBlocks.Length);
        Assert.IsTrue(textBlocks.All(element =>
            RequiredAttribute(element, "HorizontalAlignment") == "Right"
            && RequiredAttribute(element, "TextAlignment") == "Right"
            && RequiredAttribute(element, "SizeChanged") == "StatText_SizeChanged"));
        StringAssert.Contains(RequiredAttribute(textBlocks[0], "Text"), "AddedText");
        StringAssert.Contains(RequiredAttribute(textBlocks[1], "Text"), "RemovedText");
    }

    [TestMethod]
    [DataRow("Controls", "CommitBrowserView.xaml", "HistoryFileContextMenu")]
    [DataRow("Pages", "ChangesPage.xaml", "ChangesFileContextMenu")]
    public void FileMenus_KeepViewModesTogetherBeforeCopy(string folder, string file, string menuName)
    {
        XDocument document = LoadApplicationXaml(folder, file);
        XElement menu = document.Descendants().Single(element =>
            element.Name.LocalName == "MenuFlyout"
            && (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == menuName);
        XElement[] items = menu.Elements().ToArray();
        int fullFileIndex = Array.FindIndex(items, item => (string?)item.Attribute("Tag") == "FileContent");
        Assert.IsTrue(fullFileIndex > 0);
        Assert.AreEqual("MenuFlyoutSeparator", items[fullFileIndex - 1].Name.LocalName);
        Assert.AreEqual("MenuFlyoutItem", items[fullFileIndex + 1].Name.LocalName);
        Assert.AreEqual("ShowFoldersMenuFlyoutItem", RequiredXamlAttribute(items[fullFileIndex + 1], "Uid"));
        StringAssert.Contains(RequiredAttribute(items[fullFileIndex + 1], "Command"), "ToggleFoldersCommand");
        Assert.AreEqual("MenuFlyoutSeparator", items[fullFileIndex + 2].Name.LocalName);
        Assert.AreEqual("CopyName", RequiredAttribute(items[fullFileIndex + 3], "Tag"));
        Assert.IsFalse(menu.Descendants().Any(element => element.Name.LocalName == "ToggleMenuFlyoutItem"));
        XElement icon = items[fullFileIndex + 1].Descendants().Single(element => element.Name.LocalName == "SymbolIcon");
        StringAssert.Contains(RequiredAttribute(icon, "Symbol"), "FolderViewSymbol");
        StringAssert.Contains(RequiredAttribute(items[fullFileIndex], "Text"), "{x:Bind ViewModel.ContextMenuDisplayModeText");
        Assert.IsTrue(menu.Descendants().Attributes("Command").All(attribute => attribute.Value.StartsWith("{x:Bind", StringComparison.Ordinal)));
        if (menuName == "HistoryFileContextMenu")
        {
            Assert.AreEqual("FileFilterModeMenuFlyoutItem", RequiredXamlAttribute(items[0], "Uid"));
        }
    }

    [TestMethod]
    public void FileTree_UsesCompactSingleLineRowsAndKeepsFolderMenuAvailable()
    {
        XDocument document = LoadApplicationXaml("Controls", "ChangedFilesTree.xaml");
        XElement row = document.Descendants().Single(element => element.Name.LocalName == "Grid");
        Assert.AreEqual("24", RequiredAttribute(row, "Height"));
        Assert.IsFalse(document.Descendants().Any(element => element.Name.LocalName == "StackPanel"));
        XElement name = document.Descendants().Single(element =>
            element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "{x:Bind Name}");
        Assert.AreEqual("1", RequiredAttribute(name, "MaxLines"));
        Assert.AreEqual("NoWrap", RequiredAttribute(name, "TextWrapping"));
        Assert.IsTrue(document.Descendants().Where(element => element.Name.LocalName == "Border" && element.Attribute("Width") is not null)
            .All(element => RequiredAttribute(element, "Width") == "18"
                && RequiredAttribute(element, "Height") == "18"));
        XElement folderToggle = document.Descendants().Single(element =>
            element.Name.LocalName == "MenuFlyoutItem");
        Assert.AreEqual("ShowFoldersMenuFlyoutItem", RequiredXamlAttribute(folderToggle, "Uid"));
    }

    [TestMethod]
    public void FileTree_SelectionCanBeRestoredAfterNativeCollectionBecomesAvailable()
    {
        GitChangedFile selectedFile = new("src/nested/File.cs", "Modified", state: GitChangeState.Staged);
        ChangedFileTreeViewModel model = new();
        model.Refresh([selectedFile]);
        ChangedFileTreeItemViewModel leaf = model.Roots.Single().Children.Single().Children.Single();

        // Initialization must preserve the bound selection until the list is available.
        RestoreSelection(null, model.VisibleItems, new[] { selectedFile });

        List<object> nativeSelection = [new object()];
        RestoreSelection(nativeSelection, model.VisibleItems, new[] { selectedFile });
        CollectionAssert.AreEqual(new object[] { leaf }, nativeSelection);

        RestoreSelection(nativeSelection, model.VisibleItems, Array.Empty<GitChangedFile>());
        Assert.IsEmpty(nativeSelection);
    }

    [TestMethod]
    public void FileTree_SelectionRestoresOnlyRequestedFileStates()
    {
        GitChangedFile staged = new("src/File.cs", "Modified", state: GitChangeState.Staged);
        GitChangedFile unstaged = new("src/File.cs", "Modified", state: GitChangeState.Unstaged);
        ChangedFileTreeViewModel model = new();
        model.Refresh([staged, unstaged, new("other.txt", "Modified")]);
        List<object> nativeSelection = [];

        RestoreSelection(nativeSelection, model.VisibleItems, new[] { unstaged });

        Assert.HasCount(1, nativeSelection);
        ChangedFileTreeItemViewModel leaf = (ChangedFileTreeItemViewModel)nativeSelection.Single();
        Assert.AreSame(unstaged, leaf.File);
    }

    [TestMethod]
    [DataRow(16, 10, 26)]
    [DataRow(12, 10, 22)]
    [DataRow(32, 10, 42)]
    public void FileTree_ContentPaddingAlignsWithHeaderInWideAndNarrowLayouts(
        double headerRight, double statisticsRight, double expectedRight)
    {
        Microsoft.UI.Xaml.Thickness padding = ChangedFilesTree.GetContentPadding(
            new Microsoft.UI.Xaml.Thickness(0, 0, headerRight, 0),
            new Microsoft.UI.Xaml.Thickness(0, 0, statisticsRight, 0));

        Assert.AreEqual(expectedRight, padding.Right);
        Assert.AreEqual(0.0, padding.Left);
    }

    [TestMethod]
    public void FileTree_ContextMenuIsAttachedToTheTypedRowAndStatisticsUseOwnerMetrics()
    {
        XDocument document = LoadApplicationXaml("Controls", "ChangedFilesTree.xaml");
        XElement item = document.Descendants().Single(element => element.Name.LocalName == "Grid");
        Assert.AreEqual("{x:Bind}", RequiredAttribute(item, "Tag"));
        Assert.AreEqual("FileItem_ContextRequested", RequiredAttribute(item, "ContextRequested"));
        Assert.IsFalse(document.Descendants().Any(element => element.Name.LocalName == "TreeView.ContextFlyout"));
        XElement stats = document.Descendants().Single(element => element.Name.LocalName == "DiffStatPresenter");
        Assert.AreEqual("StatPresenter_Loaded", RequiredAttribute(stats, "Loaded"));
    }

    [TestMethod]
    public void FileTree_UsesNativeExtendedSelectionWithoutCheckboxes()
    {
        XDocument document = LoadApplicationXaml("Controls", "ChangedFilesTree.xaml");
        XElement list = document.Descendants().Single(element => element.Name.LocalName == "ListView");
        Assert.AreEqual("False", RequiredAttribute(list, "IsMultiSelectCheckBoxEnabled"));
        XElement containerStyle = list.Descendants().Single(element => element.Name.LocalName == "Style"
            && (string?)element.Attribute("TargetType") == "ListViewItem");
        Assert.AreEqual("{StaticResource DefaultListViewItemStyle}", RequiredAttribute(containerStyle, "BasedOn"));
        Assert.IsFalse(containerStyle.Descendants().Any(element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Template"));
        XElement checkMark = document.Descendants().Single(element =>
            (string?)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))
                == "ListViewItemSelectionCheckMarkVisualEnabled");
        Assert.AreEqual("False", checkMark.Value);
        StringAssert.Contains(RequiredAttribute(list, "ItemsSource"), "Model.VisibleItems");
        XDocument changes = LoadApplicationXaml("Pages", "ChangesPage.xaml");
        XElement tree = changes.Descendants().Single(element => element.Name.LocalName == "ChangedFilesTree");
        Assert.AreEqual("Extended", RequiredAttribute(tree, "SelectionMode"));
    }

    [TestMethod]
    public void FileTree_StagedBadgeMatchesTheFlatList()
    {
        XDocument tree = LoadApplicationXaml("Controls", "ChangedFilesTree.xaml");
        XDocument flat = LoadApplicationXaml("Pages", "ChangesPage.xaml");
        XElement treeText = tree.Descendants().Single(element => element.Name.LocalName == "TextBlock"
            && ((string?)element.Attribute("Text"))?.Contains("StateLabel", StringComparison.Ordinal) == true);
        XElement flatText = flat.Descendants().Single(element => element.Name.LocalName == "TextBlock"
            && ((string?)element.Attribute("Text"))?.Contains("StateLabel", StringComparison.Ordinal) == true);
        Assert.AreEqual("Border", treeText.Parent!.Name.LocalName);
        foreach (string attribute in new[] { "Padding", "Margin", "Background", "CornerRadius" })
        {
            Assert.AreEqual(RequiredAttribute(flatText.Parent!, attribute), RequiredAttribute(treeText.Parent, attribute));
        }
        foreach (string attribute in new[] { "FontSize", "Style", "Foreground", "VerticalAlignment", "HorizontalAlignment" })
        {
            Assert.AreEqual(RequiredAttribute(flatText, attribute), RequiredAttribute(treeText, attribute));
        }
    }

    [TestMethod]
    public void FileTree_RestoringTheSameSelectionPreservesItsOrderAndDoesNotResetTheRangeAnchor()
    {
        GitChangedFile first = new("First.cs", "Modified");
        GitChangedFile last = new("Last.cs", "Modified");
        ChangedFileTreeViewModel model = new();
        model.Refresh([first, last]);
        System.Collections.ObjectModel.ObservableCollection<object> selected = [model.VisibleItems[1], model.VisibleItems[0]];
        int changes = 0;
        selected.CollectionChanged += (_, _) => changes++;

        RestoreSelection(selected, model.VisibleItems, new[] { first, last });

        Assert.AreEqual(0, changes);
        Assert.AreSame(model.VisibleItems[1], selected[0]);
        Assert.AreSame(model.VisibleItems[0], selected[1]);
    }

    [TestMethod]
    public void FileTree_RestoringSelectionIgnoresFoldersAndFilesHiddenByCollapsedDirectories()
    {
        GitChangedFile file = new("src/File.cs", "Modified");
        ChangedFileTreeViewModel model = new();
        model.Refresh([file]);
        model.Roots.Single().IsExpanded = false;
        List<object> selected = [new object()];

        RestoreSelection(selected, model.VisibleItems, new[] { file });

        Assert.IsEmpty(selected);
    }

    [TestMethod]
    public void FileTree_FolderExpansionUsesButtonCompatibleWithItsStyle()
    {
        XDocument document = LoadApplicationXaml("Controls", "ChangedFilesTree.xaml");
        XElement expansion = document.Descendants().Single(element =>
            ((string?)element.Attribute("Command"))?.Contains("ToggleFolderExpansionCommand", StringComparison.Ordinal) == true);

        Assert.AreEqual(nameof(Microsoft.UI.Xaml.Controls.Button), expansion.Name.LocalName);
        Assert.AreEqual("{StaticResource SubtleButtonStyle}", RequiredAttribute(expansion, "Style"));
        Assert.IsNull(expansion.Attribute("IsChecked"));
        Assert.IsFalse(document.Descendants().Any(element => element.Name.LocalName is "ToggleButton" or "CheckBox"));
    }

    [TestMethod]
    [DataRow(400, 16, 10, 10, 16)]
    [DataRow(400, 12, 10, 10, 12)]
    [DataRow(800, 32, 10, 30, 12)]
    [DataRow(800, 32, 10, 28, 14)]
    public void FileTree_SelectionFillsTheSameWidthAsFlatRowsWhileStatisticsStayAligned(
        double paneWidth, double headerRight, double statisticsRight, double listRight, double expectedItemRight)
    {
        Microsoft.UI.Xaml.Thickness listPadding = new(0, 0, listRight, 0);
        Microsoft.UI.Xaml.Thickness contentPadding = ChangedFilesTree.GetContentPadding(
            new(0, 0, headerRight, 0), new(0, 0, statisticsRight, 0));
        Microsoft.UI.Xaml.Thickness itemPadding = ChangedFilesTree.GetItemPadding(contentPadding, listPadding);

        Assert.AreEqual(expectedItemRight, itemPadding.Right);
        double selectionRight = paneWidth - listPadding.Right;
        double fileStatisticsRight = selectionRight - itemPadding.Right;
        Assert.AreEqual(paneWidth - headerRight - statisticsRight, fileStatisticsRight);
        Assert.IsGreaterThan(fileStatisticsRight, selectionRight);
    }

    [TestMethod]
    public void FileTree_TransitioningBetweenLayoutsDoesNotCreateNegativeItemPadding()
    {
        Microsoft.UI.Xaml.Thickness itemPadding = ChangedFilesTree.GetItemPadding(
            new(0, 0, 10, 0), new(0, 0, 28, 0));
        Assert.AreEqual(0.0, itemPadding.Right);
    }

    private static void AssertUsesSharedMetrics(XElement presenter, string metricsKey)
    {
        Assert.AreEqual(
            "{StaticResource " + metricsKey + "}",
            RequiredAttribute(presenter, "ColumnMetrics"));
        Assert.AreEqual("Right", RequiredAttribute(presenter, "HorizontalAlignment"));
    }

    private static string RequiredAttribute(XElement element, string name)
    {
        return element.Attribute(name)?.Value
            ?? throw new AssertFailedException($"{element.Name.LocalName} must define {name}.");
    }

    private static string RequiredXamlAttribute(XElement element, string name)
    {
        XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        return element.Attribute(xamlNamespace + name)?.Value
            ?? throw new AssertFailedException($"{element.Name.LocalName} must define x:{name}.");
    }

    private static XDocument LoadApplicationXaml(params string[] relativeSegments)
    {
        string path = Path.Combine([FindRepositoryRoot(), "SimpleGit11", .. relativeSegments]);
        return XDocument.Load(path, LoadOptions.SetLineInfo);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SimpleGit11", "SimpleGit11.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new AssertFailedException($"Could not locate the repository root from {AppContext.BaseDirectory}.");
    }
}
