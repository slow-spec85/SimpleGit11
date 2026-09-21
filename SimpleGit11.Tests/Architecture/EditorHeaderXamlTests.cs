using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SimpleGit11.Tests.Architecture;

[TestClass]
public sealed class EditorHeaderXamlTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void CommitBrowserFilterFlyout_BindsEveryFilterAndResetAction()
    {
        XDocument document = LoadApplicationXaml("Controls", "CommitBrowserView.xaml");
        XElement filterToggle = FindByXName(document, "CommitFilterToggleButton");

        Assert.IsNotNull(filterToggle.Descendants().SingleOrDefault(element => element.Name.LocalName == "Flyout"));
        AssertBinding(document, "MainlineOnlyToggleSwitch", "IsOn", "ViewModel.IsMainlineOnly");
        AssertBinding(document, "CommitDateFromPicker", "Date", "ViewModel.FilterFromDate");
        AssertDescendantBinding(
            document,
            "CommitTimeFromPicker",
            "TimePickerFlyout",
            "Time",
            "ViewModel.FilterFromTime");
        AssertBinding(document, "ClearCommitDateFromButton", "Command", "ViewModel.ClearFilterFromDateCommand");
        AssertBinding(document, "CommitDateToPicker", "Date", "ViewModel.FilterToDate");
        AssertDescendantBinding(
            document,
            "CommitTimeToPicker",
            "TimePickerFlyout",
            "Time",
            "ViewModel.FilterToTime");
        AssertBinding(document, "ClearCommitDateToButton", "Command", "ViewModel.ClearFilterToDateCommand");
        AssertBinding(document, "HistorySearchTextBox", "Text", "ViewModel.SearchText");
        AssertBinding(document, "ResetCommitFiltersButton", "Command", "ViewModel.ResetCommitFiltersCommand");
    }

    [TestMethod]
    public void CommitBrowserCommitList_UsesTypedGraphRowsWithoutReplacingListVirtualization()
    {
        XDocument document = LoadApplicationXaml("Controls", "CommitBrowserView.xaml");
        XElement list = FindByXName(document, "HistoryCommitsListView");
        XElement template = list.Descendants().Single(element => element.Name.LocalName == "DataTemplate"
            && RequiredXamlAttribute(element, "DataType").Contains("CommitBrowserRowViewItem", StringComparison.Ordinal));
        XElement graph = template.Descendants().Single(element => element.Name.LocalName == "CommitGraphCell");

        StringAssert.Contains(RequiredAttribute(list, "ItemsSource"), "ViewModel.CommitRows");
        StringAssert.Contains(RequiredAttribute(list, "SelectedItem"), "ViewModel.SelectedCommitRow");
        StringAssert.Contains(RequiredAttribute(graph, "Graph"), "Graph");
        StringAssert.Contains(RequiredAttribute(graph, "Visibility"), "ShowGraph");
    }

    [TestMethod]
    public void CommitBrowserGraphToggle_IsLastHistoryContextMenuCommand()
    {
        XDocument browserDocument = LoadApplicationXaml("Controls", "CommitBrowserView.xaml");
        XDocument historyDocument = LoadApplicationXaml("Pages", "HistoryPage.xaml");
        XElement graph = FindByXName(historyDocument, "CommitGraphMenuFlyoutItem");
        XElement menu = graph.Parent!;

        Assert.IsFalse(browserDocument.Descendants().Any(element =>
            (string?)element.Attribute(XamlNamespace + "Name") == "CommitGraphToggleButton"));
        Assert.AreEqual("MenuFlyout", menu.Name.LocalName);
        Assert.AreSame(graph, menu.Elements().Last());
        Assert.AreEqual("MenuFlyoutSeparator", graph.ElementsBeforeSelf().Last().Name.LocalName);
        StringAssert.Contains(RequiredAttribute(graph, "Command"), "ViewModel.ToggleCommitGraphCommand");
        StringAssert.Contains(RequiredAttribute(graph, "Text"), "ViewModel.CommitGraphToggleText");
        Assert.AreEqual("CommitGraphMenuFlyoutItem", RequiredAttribute(graph, "AutomationProperties.AutomationId"));
    }

    [TestMethod]
    public void CommitDetailsToggle_PlacesDetailsAboveDiffWithoutReplacingChangedFiles()
    {
        XDocument document = LoadApplicationXaml("Controls", "CommitBrowserView.xaml");
        XElement filterToggle = FindByXName(document, "CommitFilterToggleButton");
        XElement detailsToggle = FindByXName(document, "CommitDetailsToggleButton");
        XElement rightPane = FindByXName(document, "HistoryRightPane");
        XElement details = FindByXName(document, "CommitDetailsHeader").Parent!;
        XElement splitter = FindByXName(document, "CommitDetailsSplitter");
        XElement diff = FindByXName(document, "HistoryDiffPane");
        XElement metadata = FindByXName(document, "CommitMetadataTextBlock");

        Assert.AreSame(detailsToggle, filterToggle.ElementsAfterSelf().First());
        StringAssert.Contains(RequiredAttribute(detailsToggle, "IsChecked"), "ViewModel.IsCommitDetailsBlockVisible");
        Assert.AreSame(rightPane, details.Parent);
        Assert.AreSame(rightPane, splitter.Parent);
        Assert.AreSame(rightPane, diff.Parent);
        Assert.AreEqual("0", RequiredAttribute(details, "Grid.Row"));
        Assert.AreEqual("1", RequiredAttribute(splitter, "Grid.Row"));
        Assert.AreEqual("2", RequiredAttribute(diff, "Grid.Row"));
        Assert.AreEqual("0", RequiredAttribute(FindByXName(document, "CommitDetailsRow"), "Height"));
        Assert.AreEqual("0", RequiredAttribute(FindByXName(document, "CommitDetailsSplitterRow"), "Height"));
        StringAssert.Contains(RequiredAttribute(details, "Visibility"), "CommitDetailsBlockVisibility");
        StringAssert.Contains(RequiredAttribute(metadata, "TextWrapping"), "Wrap");
        Assert.AreEqual("100", RequiredAttribute(FindByXName(document, "ParentCommitsItemsControl").Parent!, "MaxHeight"));
        CollectionAssert.AreEqual(
            new[] { "ViewModel.SelectedCommitDate", "ViewModel.SelectedCommitAuthor", "ViewModel.SelectedCommitHash" },
            metadata.Elements().Where(element => element.Name.LocalName == "Run")
                .Select(element => RequiredAttribute(element, "Text"))
                .Where(text => text.Contains("ViewModel.", StringComparison.Ordinal))
                .Select(text => text.TrimStart('{').Split(',')[0].Replace("x:Bind ", ""))
                .ToArray());
        Assert.IsNotNull(FindByXName(document, "HistoryChangedFilesListView"));
        AssertVisualStateSetter(document, "HistoryRightPane.(Grid.Row)", "2");
    }

    [TestMethod]
    public void CommitMessageContextMenu_OffersConditionalEditAndCopyWithoutHeaderButton()
    {
        XDocument document = LoadApplicationXaml("Controls", "CommitBrowserView.xaml");
        XElement message = FindByXName(document, "CommitMessageTextBox");
        XElement flyout = message.Descendants().Single(element => element.Name.LocalName == "CommandBarFlyout");
        XElement edit = FindByXName(document, "EditCommitMessageContextButton");
        XElement copy = FindByXName(document, "CopyCommitMessageButton");

        Assert.AreEqual("True", RequiredAttribute(message, "IsReadOnly"));
        Assert.AreEqual("CommitMessageContextFlyout_Opening", RequiredAttribute(flyout, "Opening"));
        Assert.AreSame(flyout, edit.Parent!.Parent);
        StringAssert.Contains(RequiredAttribute(edit, "Command"), "ViewModel.EditCommitMessageCommand");
        Assert.AreEqual("Collapsed", RequiredAttribute(edit, "Visibility"));
        Assert.AreEqual("Collapsed", RequiredAttribute(FindByXName(document, "CommitMessageEditSeparator"), "Visibility"));
        Assert.AreEqual("CopyCommitMessageButton_Click", RequiredAttribute(copy, "Click"));
        Assert.IsFalse(document.Descendants().Any(element =>
            (string?)element.Attribute(XamlNamespace + "Uid") == "EditCommitMessageAppBarButton"));
        Assert.IsFalse(message.Elements().Any(element => element.Name.LocalName == "TextBox.SelectionFlyout"));
    }

    [TestMethod]
    public void DiffViewerHeader_SearchAndAdaptiveControlsRemainAvailable()
    {
        XDocument document = LoadApplicationXaml("Controls", "DiffViewer.xaml");
        XElement searchToggle = FindByXName(document, "DiffSearchToggleButton");
        XElement viewOptions = FindByXName(document, "DiffViewOptionsButton");
        XElement controlsContainer = FindByXName(document, "ControlsContainer");
        XElement toolbar = controlsContainer.Elements().Single(element => element.Name.LocalName == "StackPanel");
        XElement[] toolbarControls = toolbar.Elements().ToArray();

        AssertSearchFlyout(
            searchToggle,
            "DiffSearchTextBox",
            "PreviousDiffSearchMatchButton",
            "NextDiffSearchMatchButton");
        CollectionAssert.AreEqual(
            new[]
            {
                "DiffSearchToggleButton",
                "DiffViewOptionsButton",
                "RepositoryEditorZoomControl"
            },
            toolbarControls.Take(3).Select(GetControlIdentifier).ToArray());
        Assert.AreEqual("Button", viewOptions.Name.LocalName);
        Assert.IsFalse(viewOptions.Elements().Any(element =>
            element.Name.LocalName is "SymbolIcon" or "FontIcon" or "PathIcon"));
        Assert.AreEqual("DiffViewOptionsFlyout_Opening", RequiredAttribute(
            viewOptions.Descendants().Single(element => element.Name.LocalName == "Flyout"),
            "Opening"));
        Assert.AreEqual("FullFileModeToggleSwitch_Toggled", RequiredAttribute(
            FindByXName(document, "FullFileModeToggleSwitch"),
            "Toggled"));
        StringAssert.Contains(
            RequiredAttribute(FindByXName(document, "FullFileModeToggleSwitch"), "IsOn"),
            "IsFullFileMode");
        Assert.IsTrue(viewOptions.Descendants().Contains(FindByXName(document, "IgnoreWhitespaceToggleSwitch")));
        Assert.IsTrue(viewOptions.Descendants().Contains(FindByXName(document, "FullFileModeToggleSwitch")));
        Assert.IsTrue(viewOptions.Descendants().Contains(FindByXName(document, "DiffViewerSyntaxHighlightingComboBox")));
        AssertVisualStateSetter(document, "NarrowLayoutControlsRow.Height", "Auto");
        AssertVisualStateSetter(document, "ControlsContainer.(Grid.Row)", "1");
    }

    [TestMethod]
    public void DiffViewerEditor_AccountsForLineNumberRenderingInset()
    {
        XDocument document = LoadApplicationXaml("Controls", "DiffViewer.xaml");
        XElement editor = FindByXName(document, "EditorSurface");

        Assert.AreEqual("16", RequiredAttribute(editor, "SpaceBetweenLineNumberAndText"));
    }

    [TestMethod]
    public void ConflictEditorHeader_UsesFileNameAndAdaptiveIconToolbarInRequiredOrder()
    {
        XDocument document = LoadApplicationXaml("Controls", "ConflictEditor.xaml");
        XElement heading = FindByXName(document, "ConflictFileHeading");
        XElement controlsContainer = FindByXName(document, "ControlsContainer");
        XElement toolbar = controlsContainer.Elements().Single(element => element.Name.LocalName == "StackPanel");

        StringAssert.Contains(RequiredAttribute(heading, "Text"), "ViewModel.FileName");
        StringAssert.Contains(RequiredAttribute(heading, "ToolTipService.ToolTip"), "ViewModel.RelativePath");
        Assert.IsFalse(document.Descendants().Any(element => element.Name.LocalName == "CommandBar"));
        Assert.IsFalse(document.Descendants().Any(element => element.Name.LocalName == "AppBarButton"));

        string[] identifiers = toolbar.Elements()
            .Select(GetControlIdentifier)
            .ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "ConflictSearchToggleButton",
                "ConflictEditorSyntaxHighlightingComboBox",
                "RepositoryEditorZoomControl",
                "UndoEditButton",
                "RedoEditButton",
                "ReloadConflictFileButton",
                "AcceptConflictButton",
                "MarkResolvedButton",
                "SaveConflictFileButton"
            },
            identifiers);

        foreach (XElement control in toolbar.Elements().Where(IsFixedHeightToolbarControl))
        {
            Assert.AreEqual("32", RequiredAttribute(control, "Height"), GetControlIdentifier(control));
            Assert.AreEqual("Center", RequiredAttribute(control, "VerticalAlignment"), GetControlIdentifier(control));
        }

        XElement zoomDocument = LoadApplicationXaml("Controls", "RepositoryEditorZoomControl.xaml").Root!;
        XElement zoomButton = zoomDocument.Elements().Single(element => element.Name.LocalName == "Button");
        Assert.AreEqual("32", RequiredAttribute(zoomButton, "Height"));

        AssertSearchFlyout(
            FindByXName(document, "ConflictSearchToggleButton"),
            "ConflictSearchTextBox",
            "PreviousConflictSearchMatchButton",
            "NextConflictSearchMatchButton");
        AssertVisualStateSetter(document, "EditorHeaderControlsRow.Height", "Auto");
        AssertVisualStateSetter(document, "ControlsContainer.(Grid.Row)", "1");
    }

    [TestMethod]
    public void ConflictAcceptButton_IsIconOnlyAndAlwaysOpensChoiceMenu()
    {
        XDocument document = LoadApplicationXaml("Controls", "ConflictEditor.xaml");
        XElement acceptButton = FindByXUid(document, "AcceptConflictButton");

        Assert.AreEqual("Button", acceptButton.Name.LocalName);
        Assert.IsNull(acceptButton.Attribute("Click"));
        Assert.IsNull(acceptButton.Attribute("Content"));
        Assert.IsTrue(acceptButton.Elements().Any(element =>
            element.Name.LocalName is "SymbolIcon" or "FontIcon"));

        XElement menu = acceptButton.Descendants().Single(element => element.Name.LocalName == "MenuFlyout");
        string[] menuItems = menu.Elements()
            .Where(element => element.Name.LocalName == "MenuFlyoutItem")
            .Select(element => RequiredXamlAttribute(element, "Uid"))
            .ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "AcceptCurrentConflictBlockMenuFlyoutItem",
                "AcceptIncomingConflictBlockMenuFlyoutItem",
                "AcceptBothConflictBlockMenuFlyoutItem"
            },
            menuItems);
    }

    private static void AssertSearchFlyout(
        XElement searchToggle,
        string textBoxName,
        string previousButtonName,
        string nextButtonName)
    {
        Assert.AreEqual("ToggleButton", searchToggle.Name.LocalName);
        Assert.IsNotNull(searchToggle.Descendants().SingleOrDefault(element => element.Name.LocalName == "Flyout"));
        Assert.IsNotNull(searchToggle.Descendants().SingleOrDefault(element =>
            element.Name.LocalName == "TextBox"
            && RequiredXamlAttribute(element, "Name") == textBoxName));
        Assert.IsNotNull(searchToggle.Descendants().SingleOrDefault(element =>
            element.Name.LocalName == "Button"
            && RequiredXamlAttribute(element, "Name") == previousButtonName));
        Assert.IsNotNull(searchToggle.Descendants().SingleOrDefault(element =>
            element.Name.LocalName == "Button"
            && RequiredXamlAttribute(element, "Name") == nextButtonName));
    }

    private static void AssertBinding(
        XDocument document,
        string xUidOrName,
        string attributeName,
        string expectedPath)
    {
        XElement element = FindByXUidOrName(document, xUidOrName);
        StringAssert.Contains(RequiredAttribute(element, attributeName), expectedPath);
    }

    private static void AssertDescendantBinding(
        XDocument document,
        string xUidOrName,
        string descendantType,
        string attributeName,
        string expectedPath)
    {
        XElement element = FindByXUidOrName(document, xUidOrName);
        XElement descendant = element.Descendants().Single(child => child.Name.LocalName == descendantType);
        StringAssert.Contains(RequiredAttribute(descendant, attributeName), expectedPath);
    }

    private static void AssertVisualStateSetter(XDocument document, string target, string value)
    {
        XElement? setter = document.Descendants().SingleOrDefault(element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Target") == target
            && (string?)element.Attribute("Value") == value);
        Assert.IsNotNull(setter, $"Expected visual-state setter {target}={value}.");
    }

    private static bool IsFixedHeightToolbarControl(XElement element)
    {
        return element.Name.LocalName is "Button" or "ToggleButton" or "ComboBox";
    }

    private static string GetControlIdentifier(XElement element)
    {
        return (string?)element.Attribute(XamlNamespace + "Name")
            ?? (string?)element.Attribute(XamlNamespace + "Uid")
            ?? element.Name.LocalName;
    }

    private static XElement FindByXUidOrName(XDocument document, string value)
    {
        return document.Descendants().Single(element =>
            (string?)element.Attribute(XamlNamespace + "Uid") == value
            || (string?)element.Attribute(XamlNamespace + "Name") == value);
    }

    private static XElement FindByXName(XDocument document, string name)
    {
        return document.Descendants().Single(element =>
            (string?)element.Attribute(XamlNamespace + "Name") == name);
    }

    private static XElement FindByXUid(XDocument document, string uid)
    {
        return document.Descendants().Single(element =>
            (string?)element.Attribute(XamlNamespace + "Uid") == uid);
    }

    private static string RequiredAttribute(XElement element, string name)
    {
        return element.Attribute(name)?.Value
            ?? throw new AssertFailedException($"{GetControlIdentifier(element)} must define {name}.");
    }

    private static string RequiredXamlAttribute(XElement element, string name)
    {
        return element.Attribute(XamlNamespace + name)?.Value
            ?? throw new AssertFailedException($"{element.Name.LocalName} must define x:{name}.");
    }

    private static XDocument LoadApplicationXaml(params string[] relativeSegments)
    {
        string repositoryRoot = FindRepositoryRoot();
        string path = Path.Combine([repositoryRoot, "SimpleGit11", .. relativeSegments]);
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
