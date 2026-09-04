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
            LoadApplicationXaml("Controls", "CommitBrowserView.xaml"),
            ("HistoryListsColumn.Width", "400"),
            ("HistoryDiffColumn.Width", "*"),
            ("HistoryListsRow.Height", "*"),
            ("HistoryDiffRow.Height", "0"));
        AssertWideLayoutSetters(
            LoadApplicationXaml("Pages", "BranchesPage.xaml"),
            ("ListColumn.Width", "380"),
            ("DetailsColumn.Width", "*"),
            ("ListRow.Height", "*"),
            ("DetailsRow.Height", "0"));
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
