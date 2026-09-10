using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SimpleGit11.Tests.Architecture;

[TestClass]
public sealed class OpenRepositoryInNewWindowXamlTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    [DataRow("OpenRepositoryInNewWindowAppBarButton")]
    [DataRow("OpenSubmoduleInNewWindowAppBarButton")]
    [DataRow("OpenFoundRepositoryInNewWindowAppBarButton")]
    public void RepositoryPage_NewWindowCommands_AreFollowedBySeparator(string uid)
    {
        XDocument document = LoadApplicationXml("Pages", "RepositoryPage.xaml");

        AssertFollowedBySeparator(document, uid, "AppBarSeparator");
    }

    [TestMethod]
    public void RecentRepository_NewWindowCommand_IsFollowedBySeparator()
    {
        XDocument document = LoadApplicationXml("MainWindow.xaml");

        AssertFollowedBySeparator(
            document,
            "OpenRecentRepositoryInNewWindowMenuFlyoutItem",
            "MenuFlyoutSeparator");
    }

    [TestMethod]
    [DataRow("en-US")]
    [DataRow("ru-RU")]
    public void NewWindowCommands_AreLocalized(string language)
    {
        XDocument document = LoadApplicationXml("Strings", language, "Resources.resw");
        HashSet<string> resourceNames = document.Root!.Elements("data")
            .Select(element => element.Attribute("name")!.Value)
            .ToHashSet(StringComparer.Ordinal);
        string[] expectedResources =
        [
            "OpenRepositoryInNewWindowAppBarButton.Label",
            "OpenRecentRepositoryInNewWindowMenuFlyoutItem.Text",
            "OpenSubmoduleInNewWindowAppBarButton.Label",
            "OpenFoundRepositoryInNewWindowAppBarButton.Label"
        ];

        foreach (string resourceName in expectedResources)
        {
            Assert.Contains(resourceName, resourceNames);
        }
    }

    private static void AssertFollowedBySeparator(
        XDocument document,
        string uid,
        string separatorName)
    {
        XElement command = document.Descendants().Single(element =>
            (string?)element.Attribute(XamlNamespace + "Uid") == uid);
        XElement? nextElement = command.ElementsAfterSelf().FirstOrDefault();

        Assert.IsNotNull(nextElement);
        Assert.AreEqual(separatorName, nextElement.Name.LocalName);
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

        throw new AssertFailedException(
            $"Could not locate the repository root from {AppContext.BaseDirectory}.");
    }
}
