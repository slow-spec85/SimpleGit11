using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SimpleGit11.Tests.Architecture;

[TestClass]
public sealed class XamlBindingArchitectureTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void ApplicationTheme_IsNotPinnedAtStartup()
    {
        string applicationDirectory = Path.Combine(FindRepositoryRoot(), "SimpleGit11");
        XDocument application = XDocument.Load(Path.Combine(applicationDirectory, "App.xaml"));
        Assert.IsNull(application.Root!.Attribute("RequestedTheme"),
            "The application must follow Windows so ElementTheme.Default can restore the current system theme.");

        string applicationCode = File.ReadAllText(Path.Combine(applicationDirectory, "App.xaml.cs"));
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(applicationCode, @"\bRequestedTheme\s*="),
            "Persisted theme overrides must be applied to the window, not Application.RequestedTheme.");
    }

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    [DataRow("HighContrast")]
    public void NotificationBackground_IsResolvedFromEachThemeDictionary(string theme)
    {
        XDocument overlay = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "SimpleGit11", "Controls", "NotificationOverlay.xaml"));
        XNamespace presentation = overlay.Root!.Name.Namespace;
        XElement resources = overlay.Root.Element(presentation + "UserControl.Resources")!
            .Element(presentation + "ResourceDictionary")!;
        const string brushKey = "InfoBarInformationalSeverityBackgroundBrush";
        Assert.IsFalse(resources.Elements().Any(element =>
            (string?)element.Attribute(XamlNamespace + "Key") == brushKey),
            "A shared alias would retain the brush resolved when the control was created.");

        XElement dictionary = resources.Element(presentation + "ResourceDictionary.ThemeDictionaries")!
            .Elements().Single(element => (string?)element.Attribute(XamlNamespace + "Key") == theme);
        XElement brush = dictionary.Elements().Single(element =>
            (string?)element.Attribute(XamlNamespace + "Key") == brushKey);
        if (theme == "HighContrast")
        {
            Assert.AreEqual("{ThemeResource SystemColorWindowColor}", (string?)brush.Attribute("Color"));
        }
        else
        {
            Assert.AreEqual("ApplicationPageBackgroundThemeBrush", (string?)brush.Attribute("ResourceKey"));
        }
    }

    [TestMethod]
    public void DataTemplates_DeclareXDataType()
    {
        string repositoryRoot = FindRepositoryRoot();
        string applicationDirectory = Path.Combine(repositoryRoot, "SimpleGit11");
        List<string> violations = Directory
            .EnumerateFiles(applicationDirectory, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path, applicationDirectory))
            .SelectMany(path => FindUntypedDataTemplates(path, repositoryRoot))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.AreEqual(
            0,
            violations.Count,
            $"Every DataTemplate must declare x:DataType so its item bindings can be compiled:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string projectPath = Path.Combine(directory.FullName, "SimpleGit11", "SimpleGit11.csproj");
            if (File.Exists(projectPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail($"Could not locate the repository root from {AppContext.BaseDirectory}.");
        return string.Empty;
    }

    private static bool IsBuildArtifact(string path, string applicationDirectory)
    {
        string relativePath = Path.GetRelativePath(applicationDirectory, path);
        string firstSegment = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return firstSegment is "bin" or "obj";
    }

    private static IEnumerable<string> FindUntypedDataTemplates(string path, string repositoryRoot)
    {
        XDocument document = XDocument.Load(path, LoadOptions.SetLineInfo);

        foreach (XElement dataTemplate in document.Descendants().Where(element => element.Name.LocalName == "DataTemplate"))
        {
            if (dataTemplate.Attribute(XamlNamespace + "DataType") is not null)
            {
                continue;
            }

            IXmlLineInfo lineInfo = dataTemplate;
            string relativePath = Path.GetRelativePath(repositoryRoot, path);
            yield return $"{relativePath}:{lineInfo.LineNumber}";
        }
    }
}
