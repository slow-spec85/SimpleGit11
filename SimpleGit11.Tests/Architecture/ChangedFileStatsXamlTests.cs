using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SimpleGit11.Tests.Architecture;

[TestClass]
public sealed class ChangedFileStatsXamlTests
{
    [TestMethod]
    public void CommitBrowserChangedFileStats_UseSharedPresenterMetrics()
    {
        XDocument document = LoadApplicationXaml("Controls", "CommitBrowserView.xaml");
        XElement metrics = document.Descendants().Single(element =>
            element.Name.LocalName == "DiffStatColumnMetrics");
        Assert.AreEqual("HistoryDiffStatColumnMetrics", RequiredXamlAttribute(metrics, "Key"));

        XElement[] presenters = document.Descendants()
            .Where(element => element.Name.LocalName == "DiffStatPresenter")
            .ToArray();
        Assert.AreEqual(2, presenters.Length);

        XElement headerPresenter = presenters.Single(element =>
            (string?)element.Attribute("IsColumnWidthSource") == "True");
        XElement rowPresenter = presenters.Single(element =>
            element.Attribute("IsColumnWidthSource") is null);

        AssertUsesSharedMetrics(headerPresenter);
        AssertUsesSharedMetrics(rowPresenter);
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

    private static void AssertUsesSharedMetrics(XElement presenter)
    {
        Assert.AreEqual(
            "{StaticResource HistoryDiffStatColumnMetrics}",
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
