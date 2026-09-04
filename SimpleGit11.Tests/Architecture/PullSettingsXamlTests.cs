using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SimpleGit11.Tests.Architecture;

[TestClass]
public sealed class PullSettingsXamlTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    [DataRow("Global", "Rebase")]
    [DataRow("Global", "FastForward")]
    [DataRow("Repository", "Rebase")]
    [DataRow("Repository", "FastForward")]
    public void PullCards_UseFixedWidthSelectorsAndSeparateDetails(string scope, string setting)
    {
        XDocument document = LoadApplicationXml("Pages", "SettingsPage.xaml");
        string controlName = $"{scope}Pull{setting}ComboBox";
        XElement selector = document.Descendants().Single(element =>
            (string?)element.Attribute(XamlNamespace + "Name") == controlName);
        Assert.AreEqual("180", (string?)selector.Attribute("Width"));
        Assert.IsNull(selector.Attribute("MinWidth"));
        StringAssert.Contains(selector.Attribute("SelectedItem")!.Value, "Mode=TwoWay");
        StringAssert.Contains(selector.Attribute("SelectedItem")!.Value, $"ViewModel.Selected{scope}Pull{setting}");
        StringAssert.Contains(selector.Attribute("ItemsSource")!.Value, $"ViewModel.{scope}Pull{setting}Options");
        Assert.IsFalse(document.Descendants().Any(element =>
            element.Name.LocalName == "Setter"
            && ((string?)element.Attribute("Target"))?.StartsWith(controlName + ".", StringComparison.Ordinal) == true));

        XElement card = selector.Parent!;
        StringAssert.Contains(card.Attribute("IsEnabled")!.Value, $"ViewModel.Is{scope}PullSettingsLoaded");
        XElement header = card.Elements().Single(element => element.Name.LocalName == "SettingsCard.Header");
        Assert.HasCount(2, header.Descendants().Where(element => element.Name.LocalName == "TextBlock").ToArray());
        XElement tooltip = card.Descendants().Single(element => element.Name.LocalName == "ToolTip");
        Assert.AreEqual($"Pull{setting}DetailsTextBlock", (string?)tooltip.Elements().Single().Attribute(XamlNamespace + "Uid"));
    }

    [TestMethod]
    [DataRow("en-US")]
    [DataRow("ru-RU")]
    public void PullResources_ContainOnlyValuesInOptionsAndTwoLineSummaries(string language)
    {
        XDocument document = LoadApplicationXml("Strings", language, "Resources.resw");
        Dictionary<string, string> resources = document.Root!.Elements("data").ToDictionary(
            element => element.Attribute("name")!.Value,
            element => element.Element("value")!.Value);
        Dictionary<string, string> expectedOptions = new()
        {
            ["PullMergeOption"] = "false",
            ["PullRebaseOption"] = "true",
            ["PullRebaseMergesOption"] = "merges",
            ["PullRebaseInteractiveOption"] = "interactive",
            ["PullFastForwardOption"] = "true",
            ["PullNoFastForwardOption"] = "false",
            ["PullFastForwardOnlyOption"] = "only"
        };
        foreach ((string key, string value) in expectedOptions)
        {
            Assert.AreEqual(value, resources[key]);
        }

        foreach (string setting in new[] { "Rebase", "FastForward" })
        {
            string summary = resources[$"Pull{setting}DescriptionTextBlock.Text"];
            Assert.HasCount(2, summary.Split('\n'));
            StringAssert.Contains(summary, language == "ru-RU" ? "(рекомендуется)" : "(recommended)");
            string details = resources[$"Pull{setting}DetailsTextBlock.Text"];
            Assert.IsGreaterThan(summary.Length, details.Length);
            Assert.AreEqual(details, resources[$"Pull{setting}ComboBox.ToolTipService.ToolTip"]);
        }

        Assert.IsFalse(resources.ContainsKey("PullConfigNotLoaded"));
        Assert.IsFalse(resources.ContainsKey("PullConfigNotSet"));
        Assert.IsFalse(resources.ContainsKey("PullConfigSetFormat"));
        Assert.IsFalse(resources.ContainsKey("PullExistingValueFormat"));
    }

    private static XDocument LoadApplicationXml(params string[] relativeSegments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SimpleGit11", "SimpleGit11.csproj")))
            {
                return XDocument.Load(Path.Combine([directory.FullName, "SimpleGit11", .. relativeSegments]));
            }

            directory = directory.Parent;
        }

        throw new AssertFailedException($"Could not locate the repository root from {AppContext.BaseDirectory}.");
    }
}
