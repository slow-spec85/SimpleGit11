using System.Xml.Linq;

namespace SimpleGit11.Tests.Architecture;

[TestClass]
public sealed class AboutDialogXamlTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void LatestReleaseRow_ShowsUpdateActionOnSeparateLineAfterVersion()
    {
        XDocument document = LoadApplicationXml("Dialogs", "AboutDialog.xaml");
        XElement versionLink = FindNamedElement(document, "LatestReleaseHyperlinkButton");
        XElement updateActionPanel = FindNamedElement(document, "UpdateActionPanel");
        XElement updateButton = FindNamedElement(document, "InstallUpdateButton");
        XElement updateProgressRing = FindNamedElement(document, "UpdateProgressRing");
        XElement updateStatusText = FindNamedElement(document, "UpdateStatusTextBlock");
        XElement[] releaseElements = versionLink.Parent!.Elements().ToArray();
        XElement[] actionElements = updateActionPanel.Elements().ToArray();

        Assert.AreSame(versionLink.Parent, updateActionPanel.Parent);
        Assert.IsTrue(
            Array.IndexOf(releaseElements, versionLink) < Array.IndexOf(releaseElements, updateActionPanel));
        Assert.AreSame(updateActionPanel, updateButton.Parent);
        Assert.IsTrue(
            Array.IndexOf(actionElements, updateButton) < Array.IndexOf(actionElements, updateProgressRing));
        Assert.IsTrue(
            Array.IndexOf(actionElements, updateProgressRing) < Array.IndexOf(actionElements, updateStatusText));
        StringAssert.Contains(updateButton.Attribute("Command")!.Value, "ViewModel.InstallUpdateCommand");
        StringAssert.Contains(updateActionPanel.Attribute("Visibility")!.Value, "ViewModel.CanInstallUpdate");
        Assert.IsFalse(document.Descendants().Any(element => element.Name.LocalName == "ToggleSwitch"));
    }

    [TestMethod]
    [DataRow("en-US")]
    [DataRow("ru-RU")]
    public void UpdateResources_AreLocalizedAndPrereleaseToggleIsRemoved(string language)
    {
        XDocument document = LoadApplicationXml("Strings", language, "Resources.resw");
        HashSet<string> keys = document.Root!
            .Elements("data")
            .Select(element => element.Attribute("name")!.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string key in new[]
        {
            "InstallUpdateButton.Content",
            "InstallUpdateButton.AutomationProperties.Name",
            "UpdateProgressRing.AutomationProperties.Name",
            "AboutDownloadingUpdate",
            "AboutUpdateFailed"
        })
        {
            Assert.Contains(key, keys);
        }

        Assert.IsFalse(keys.Any(key => key.StartsWith(
            "IncludePrereleaseVersionsToggleSwitch.",
            StringComparison.Ordinal)));
    }

    private static XElement FindNamedElement(XDocument document, string name)
    {
        return document.Descendants().Single(element =>
            (string?)element.Attribute(XamlNamespace + "Name") == name);
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
