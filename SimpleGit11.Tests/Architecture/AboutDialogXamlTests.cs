using System.Xml.Linq;

namespace SimpleGit11.Tests.Architecture;

[TestClass]
public sealed class AboutDialogXamlTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void LatestReleaseRow_ShowsUpdateButtonAndStatusOnNextRow()
    {
        XDocument document = LoadApplicationXml("Dialogs", "AboutDialog.xaml");
        XElement versionLink = FindNamedElement(document, "LatestReleaseHyperlinkButton");
        XElement updateActionPanel = FindNamedElement(document, "UpdateActionPanel");
        XElement updateButton = FindNamedElement(document, "InstallUpdateButton");
        XElement updateProgressRing = FindNamedElement(document, "UpdateProgressRing");
        XElement updateStatusText = FindNamedElement(document, "UpdateStatusTextBlock");
        XElement releasePanel = versionLink.Parent!;
        XElement layoutGrid = releasePanel.Parent!;

        Assert.AreEqual("Grid", layoutGrid.Name.LocalName);
        Assert.AreSame(layoutGrid, updateButton.Parent);
        Assert.AreSame(layoutGrid, updateActionPanel.Parent);

        int releaseRow = (int?)releasePanel.Attribute("Grid.Row") ?? 0;
        int updateRow = (int?)updateButton.Attribute("Grid.Row") ?? 0;
        Assert.AreEqual(releaseRow + 1, updateRow);
        Assert.AreEqual(updateRow, (int?)updateActionPanel.Attribute("Grid.Row") ?? 0);
        Assert.AreEqual(0, (int?)updateButton.Attribute("Grid.Column") ?? 0);
        Assert.AreEqual(1, (int?)releasePanel.Attribute("Grid.Column") ?? 0);
        Assert.AreEqual(1, (int?)updateActionPanel.Attribute("Grid.Column") ?? 0);

        Assert.AreSame(updateActionPanel, updateProgressRing.Parent);
        Assert.AreSame(updateActionPanel, updateStatusText.Parent);
        Assert.AreEqual(0, (int?)updateProgressRing.Attribute("Grid.Column") ?? 0);
        Assert.AreEqual(1, (int?)updateStatusText.Attribute("Grid.Column") ?? 0);
        StringAssert.Contains(updateButton.Attribute("Command")!.Value, "ViewModel.InstallUpdateCommand");
        StringAssert.Contains((string?)updateButton.Attribute("Visibility"), "ViewModel.CanInstallUpdate");
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
            "AboutUpdateInProgress",
            "AboutUpdateFailed",
            "AboutUpdateValidateRelease",
            "AboutUpdateDownloadChecksum",
            "AboutUpdateDownloadChecksumViaApi",
            "AboutUpdateValidateChecksum",
            "AboutUpdatePrepareDownload",
            "AboutUpdateDownloadInstaller",
            "AboutUpdateDownloadInstallerViaApi",
            "AboutUpdateVerifyInstaller",
            "AboutUpdateSaveInstaller",
            "AboutUpdateLaunchInstaller"
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
