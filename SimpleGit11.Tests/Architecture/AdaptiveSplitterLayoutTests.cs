using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SimpleGit11.Tests.Architecture;

[TestClass]
public sealed class AdaptiveSplitterLayoutTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void AdaptiveSplitters_WideLayoutRestoresPaneDimensions()
    {
        AssertWideLayoutSetters(
            LoadApplicationXaml("Pages", "ChangesPage.xaml"),
            ("FileListsColumn.Width", "400"),
            ("DiffColumn.Width", "*"),
            ("FileListsRow.Height", "*"),
            ("DiffRow.Height", "0"));
        AssertWideLayoutSetters(
            LoadApplicationXaml("Pages", "BranchesPage.xaml"),
            ("ListColumn.Width", "380"),
            ("DetailsColumn.Width", "*"),
            ("ListRow.Height", "*"),
            ("DetailsRow.Height", "0"));
    }

    [TestMethod]
    [DataRow("HistoryPage.xaml")]
    [DataRow("CommitRangePage.xaml")]
    public void CommitBrowserPages_MoveTheSharedRightAlignedToolbarBelowTheTitle(string page)
    {
        XDocument document = LoadApplicationXaml("Pages", page);
        XElement toolbar = FindNamedElement(document, "HeaderToolbar");
        XElement browser = FindNamedElement(document, "CommitBrowser");

        Assert.AreEqual("CommitBrowserToolbar", toolbar.Name.LocalName);
        Assert.AreEqual("Right", (string?)toolbar.Attribute("HorizontalAlignment"));
        Assert.AreEqual("1", (string?)toolbar.Attribute("Grid.Column"));
        Assert.AreEqual("{x:Bind ViewModel, Mode=OneWay}", (string?)toolbar.Attribute("ViewModel"));
        Assert.AreEqual((string?)toolbar.Attribute("ViewModel"), (string?)browser.Attribute("ViewModel"));
        Assert.AreEqual("CommandBar", toolbar.Elements().Single().Elements().Single().Name.LocalName);

        Dictionary<string, string> narrowSetters = FindNamedElement(document, "NarrowLayout").Descendants()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(element => (string)element.Attribute("Target")!, element => (string)element.Attribute("Value")!);
        Assert.AreEqual("1", narrowSetters["HeaderToolbar.(Grid.Row)"]);
        Assert.AreEqual("0", narrowSetters["HeaderToolbar.(Grid.Column)"]);
        Assert.AreEqual("2", narrowSetters["HeaderToolbar.(Grid.ColumnSpan)"]);
    }

    [TestMethod]
    public void CommitBrowserToolbar_SeparatesPaneTogglesFromVisiblePageCommands()
    {
        XDocument document = LoadApplicationXaml("Controls", "CommitBrowserToolbar.xaml");
        XElement viewCommands = FindNamedElement(document, "ViewCommands");
        XElement separator = FindNamedElement(document, "ViewCommandsSeparator");
        XElement actions = separator.ElementsAfterSelf().Single();

        Assert.AreEqual("Collapsed", (string?)viewCommands.Attribute("DefaultLabelPosition"));
        Assert.HasCount(4, viewCommands.Elements().Where(element => element.Name.LocalName == "AppBarToggleButton"));
        Assert.AreSame(viewCommands.Parent, separator.Parent);
        Assert.IsNull(viewCommands.Attribute("Grid.Column"));
        Assert.AreEqual("1", (string?)separator.Attribute("Grid.Column"));
        Assert.AreEqual("2", (string?)actions.Attribute("Grid.Column"));
        Assert.AreEqual("{x:Bind ActionCommands.Visibility, Mode=OneWay, FallbackValue=Collapsed}",
            (string?)separator.Attribute("Visibility"));
        Assert.AreEqual("{x:Bind ActionCommands, Mode=OneWay}", (string?)actions.Attribute("Content"));
    }

    [TestMethod]
    public void AdaptiveSplitters_DetermineOrientationFromCurrentVisualState()
    {
        AssertUsesCurrentVisualState("Pages", "ChangesPage.xaml.cs", "DiffSplitterRow.ActualHeight");
        AssertUsesCurrentVisualState("Controls", "CommitBrowserView.xaml.cs", "HistoryDiffSplitterRow.ActualHeight");
        AssertUsesCurrentVisualState("Pages", "BranchesPage.xaml.cs", "PaneSplitterRow.ActualHeight");
    }

    private static void AssertWideLayoutSetters(
        XDocument document,
        params (string Target, string Value)[] expectedSetters)
    {
        XElement wideLayout = document.Descendants().Single(element =>
            element.Name.LocalName == "VisualState"
            && (string?)element.Attribute(XamlNamespace + "Name") == "WideLayout");

        foreach ((string target, string value) in expectedSetters)
        {
            XElement? setter = wideLayout.Descendants().SingleOrDefault(element =>
                element.Name.LocalName == "Setter"
                && (string?)element.Attribute("Target") == target
                && (string?)element.Attribute("Value") == value);
            Assert.IsNotNull(setter, $"WideLayout must restore {target}={value}.");
        }
    }

    private static void AssertUsesCurrentVisualState(
        string directory,
        string fileName,
        string obsoleteLayoutCheck)
    {
        string source = File.ReadAllText(GetApplicationPath(directory, fileName));

        StringAssert.Contains(source, "LayoutStates.CurrentState?.Name == \"NarrowLayout\"");
        Assert.IsFalse(
            source.Contains(obsoleteLayoutCheck, StringComparison.Ordinal),
            $"{fileName} must not infer the active layout from measured dimensions.");
    }

    private static XDocument LoadApplicationXaml(params string[] relativeSegments)
    {
        return XDocument.Load(GetApplicationPath(relativeSegments), LoadOptions.SetLineInfo);
    }

    private static XElement FindNamedElement(XDocument document, string name)
    {
        return document.Descendants().Single(element => (string?)element.Attribute(XamlNamespace + "Name") == name);
    }

    private static string GetApplicationPath(params string[] relativeSegments)
    {
        return Path.Combine([FindRepositoryRoot(), "SimpleGit11", .. relativeSegments]);
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
