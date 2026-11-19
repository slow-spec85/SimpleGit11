using System.Xml.Linq;

namespace SimpleGit11.Tests.Architecture;

[TestClass]
public sealed class AboutDialogXamlTests
{
    [TestMethod]
    public void RepositoryLink_UsesRegularTextColorAndKeepsNavigation()
    {
        XDocument document = LoadApplicationXml("Dialogs", "AboutDialog.xaml");
        XElement link = document.Descendants().Single(element =>
            (string?)element.Attribute("AutomationProperties.AutomationId") == "AboutRepositoryLink");

        Assert.AreEqual("HyperlinkButton", link.Name.LocalName);
        Assert.AreEqual("{ThemeResource TextFillColorPrimaryBrush}", (string?)link.Attribute("Foreground"));
        StringAssert.Contains((string?)link.Attribute("NavigateUri"), "ViewModel.RepositoryUri");
    }

    [TestMethod]
    public void DialogFooter_ShowsNeutralCloseBeforeAccentUpdate()
    {
        XDocument document = LoadApplicationXml("Dialogs", "AboutDialog.xaml");
        XElement dialog = document.Root!;
        Assert.AreEqual("None", (string?)dialog.Attribute("DefaultButton"));
        Assert.IsNull(dialog.Attribute("PrimaryButtonStyle"));
        Assert.AreEqual("{ThemeResource AccentButtonStyle}", (string?)dialog.Attribute("SecondaryButtonStyle"));
        Assert.AreEqual("{x:Bind ViewModel.InstallUpdateButtonText, Mode=OneWay}",
            (string?)dialog.Attribute("SecondaryButtonText"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        Assert.IsFalse(document.Descendants().Any(element =>
            (string?)element.Attribute(xaml + "Name") == "InstallUpdateButton"));
        Assert.IsFalse(document.Descendants().Any(element => element.Name.LocalName == "ProgressBar"));
        XElement releaseCheckProgress = document.Descendants().Single(element =>
            element.Name.LocalName == "ProgressRing");
        StringAssert.Contains((string?)releaseCheckProgress.Attribute("IsActive"), "ViewModel.IsLoadingLatestRelease");
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
            "AboutDialog.PrimaryButtonText",
            "AboutInstallUpdateButtonText",
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
        Assert.DoesNotContain("UpdateProgressRing.AutomationProperties.Name", keys);
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
