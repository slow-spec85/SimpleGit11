using System.Xml.Linq;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SimpleGit11.Tests.Architecture;

[TestClass]
public sealed class BranchesDetailsXamlTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void VisibilityBindings_FallbackValuesMatchTargetTypeEvenWhenConverterIsPresent()
    {
        XDocument document = LoadBranchesPage();
        foreach (XAttribute attribute in document.Descendants().Attributes("Visibility"))
        {
            Match fallback = Regex.Match(attribute.Value, @"FallbackValue\s*=\s*([^,}]+)");
            if (!fallback.Success) continue;

            string value = fallback.Groups[1].Value.Trim().Trim('\'', '"');
            Assert.IsTrue(
                Enum.TryParse(value, out Visibility visibility) && Enum.IsDefined(visibility),
                $"Visibility fallback '{value}' on {attribute.Parent!.Name.LocalName} must be a Visibility value; converters do not convert fallback values.");
        }
    }

    [TestMethod]
    public void DetailsValues_ExposeUniversalCopyCommand()
    {
        XDocument document = LoadBranchesPage();
        string[] bindingPaths =
        [
            "ViewModel.SelectedCommitAuthor",
            "ViewModel.SelectedCommitDate",
            "ViewModel.SelectedCommitHash",
            "ViewModel.SelectedCommitMessage",
            "ViewModel.SelectedTaggerText",
            "ViewModel.SelectedTaggerDate",
            "ViewModel.SelectedTagMessage",
            "ViewModel.SelectedTagTargetCommitText",
            "ViewModel.SelectedTagTargetType"
        ];

        foreach (string bindingPath in bindingPaths)
        {
            XElement textBlock = document.Descendants().First(element =>
                element.Name.LocalName == "TextBlock"
                && ((string?)element.Attribute("Text"))?.Contains(bindingPath, StringComparison.Ordinal) == true);
            XElement copyItem = textBlock.Descendants().Single(element =>
                element.Name.LocalName == "MenuFlyoutItem"
                && (string?)element.Attribute(XamlNamespace + "Uid") == "CopyTextMenuFlyoutItem");

            StringAssert.Contains((string?)copyItem.Attribute("Command") ?? string.Empty, "ViewModel.CopyTextCommand");
            StringAssert.Contains((string?)copyItem.Attribute("CommandParameter") ?? string.Empty, bindingPath);
        }
    }

    [TestMethod]
    public void TagTargetObjectCard_OpensLoadedCommit()
    {
        XDocument document = LoadBranchesPage();
        XElement card = document.Descendants().Single(element =>
            element.Name.LocalName == "SettingsCard"
            && (string?)element.Attribute(XamlNamespace + "Uid") == "TagTargetObjectCard");

        Assert.AreEqual("True", (string?)card.Attribute("IsClickEnabled"));
        Assert.AreEqual("TagTargetObjectCard_Click", (string?)card.Attribute("Click"));
        StringAssert.Contains(
            (string?)card.Attribute("IsEnabled") ?? string.Empty,
            "ViewModel.CanOpenSelectedTagTargetCommit");
    }

    private static XDocument LoadBranchesPage()
    {
        string directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "SimpleGit11.slnx")))
        {
            DirectoryInfo? parent = Directory.GetParent(directory);
            if (parent is null)
            {
                throw new DirectoryNotFoundException("Could not locate the repository root.");
            }

            directory = parent.FullName;
        }

        return XDocument.Load(Path.Combine(directory, "SimpleGit11", "Pages", "BranchesPage.xaml"));
    }
}
