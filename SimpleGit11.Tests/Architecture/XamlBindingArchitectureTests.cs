using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SimpleGit11.Tests.Architecture;

[TestClass]
public sealed class XamlBindingArchitectureTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

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
