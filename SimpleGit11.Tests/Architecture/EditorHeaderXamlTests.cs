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
    public void DiffViewerHeader_SearchAndAdaptiveControlsRemainAvailable()
    {
        XDocument document = LoadApplicationXaml("Controls", "DiffViewer.xaml");
        XElement searchToggle = FindByXName(document, "DiffSearchToggleButton");

        AssertSearchFlyout(
            searchToggle,
            "DiffSearchTextBox",
            "PreviousDiffSearchMatchButton",
            "NextDiffSearchMatchButton");
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
