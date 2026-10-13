using System.Xml.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
using SimpleGit11.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SimpleGit11.Tests.Architecture;

[TestClass]
public sealed class OpenRepositoryInNewWindowXamlTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void RecentRepositoryCommands_ResolvePropertiesOnTheirActualOwners()
    {
        XDocument document = LoadApplicationXml("MainWindow.xaml");
        XElement list = document.Descendants().Single(element =>
            (string?)element.Attribute(XamlNamespace + "Name") == "RecentRepositoriesList");
        foreach (XAttribute command in list.Descendants().Attributes("Command"))
        {
            Match binding = Regex.Match(command.Value, @"\{Binding\s+([^,]+),\s*ElementName=RecentRepositoriesList\}");
            Assert.IsTrue(binding.Success, $"Unexpected recent repository command binding: {command.Value}");
            string path = binding.Groups[1].Value;
            Type owner;
            string property;
            if (path == "Tag")
            {
                Match compiledBinding = Regex.Match((string?)list.Attribute("Tag") ?? "", @"\{x:Bind RepositoryViewModel\.(\w+),");
                Assert.IsTrue(compiledBinding.Success, "The command on Tag must be bound to RepositoryViewModel.");
                owner = typeof(RepositoryViewModel);
                property = compiledBinding.Groups[1].Value;
            }
            else
            {
                Assert.IsTrue(path.StartsWith("DataContext.", StringComparison.Ordinal));
                owner = typeof(MainWindowViewModel);
                property = path["DataContext.".Length..];
            }
            System.Reflection.PropertyInfo? commandProperty = owner.GetProperty(property);
            Assert.IsNotNull(commandProperty, $"{owner.Name} does not expose {property}.");
            Assert.IsTrue(typeof(ICommand).IsAssignableFrom(commandProperty.PropertyType));
        }
    }

    [TestMethod]
    [DataRow("OpenSubmoduleInNewWindowActionMenuFlyoutItem")]
    [DataRow("OpenFoundRepositoryInNewWindowActionMenuFlyoutItem")]
    public void RepositoryPage_NewWindowCommands_AreFollowedBySeparator(string uid)
    {
        XDocument document = LoadApplicationXml("Pages", "RepositoryPage.xaml");

        AssertFollowedBySeparator(document, uid, "MenuFlyoutSeparator");
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
            "OpenSubmoduleInNewWindowActionMenuFlyoutItem.Text",
            "OpenFoundRepositoryInNewWindowActionMenuFlyoutItem.Text"
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
