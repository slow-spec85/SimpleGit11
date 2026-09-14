using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SimpleGit11.Tests.Architecture;

[TestClass]
public sealed partial class LocalizationResourceFormattingTests
{
    private static readonly string[] RequiredResHeaders =
    [
        "resmimetype",
        "version",
        "reader",
        "writer"
    ];

    [TestMethod]
    public void AllLocalizationResources_UseValidCanonicalFormatting()
    {
        string repositoryRoot = FindRepositoryRoot();
        List<string> violations = Directory
            .EnumerateFiles(repositoryRoot, "Resources.resw", SearchOption.AllDirectories)
            .Where(path => IsSourceResource(path, repositoryRoot))
            .SelectMany(path => FindViolations(path, repositoryRoot))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.AreEqual(
            0,
            violations.Count,
            $"Every localization resource must be valid and use the canonical multiline format:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string solutionPath = Path.Combine(directory.FullName, "SimpleGit11.slnx");
            if (File.Exists(solutionPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail($"Could not locate the repository root from {AppContext.BaseDirectory}.");
        return string.Empty;
    }

    private static bool IsSourceResource(string path, string repositoryRoot)
    {
        string relativePath = Path.GetRelativePath(repositoryRoot, path);
        string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments[0].StartsWith("SimpleGit11", StringComparison.Ordinal)
            && !segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
            && !segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> FindViolations(string path, string repositoryRoot)
    {
        string relativePath = Path.GetRelativePath(repositoryRoot, path);
        string content = File.ReadAllText(path);

        XDocument? document = null;
        XmlException? parseException = null;
        try
        {
            document = XDocument.Parse(content, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            parseException = exception;
        }

        if (parseException is not null)
        {
            yield return $"{relativePath}:{parseException.LineNumber}: invalid XML: {parseException.Message}";
            yield break;
        }

        XElement? root = document!.Root;
        if (root is null)
        {
            yield return $"{relativePath}: missing root element";
            yield break;
        }

        HashSet<string> resHeaders = root
            .Elements("resheader")
            .Select(element => (string?)element.Attribute("name"))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (string requiredResHeader in RequiredResHeaders.Where(name => !resHeaders.Contains(name)))
        {
            yield return $"{relativePath}: missing resheader '{requiredResHeader}'";
        }

        string[] lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (XElement dataElement in root.Elements("data"))
        {
            foreach (string violation in FindDataElementViolations(dataElement, lines, relativePath))
            {
                yield return violation;
            }
        }
    }

    private static IEnumerable<string> FindDataElementViolations(
        XElement dataElement,
        IReadOnlyList<string> lines,
        string relativePath)
    {
        IXmlLineInfo dataLineInfo = dataElement;
        int dataLineIndex = dataLineInfo.LineNumber - 1;
        if (dataLineIndex < 0 || dataLineIndex >= lines.Count || !DataOpeningLineRegex().IsMatch(lines[dataLineIndex]))
        {
            yield return $"{relativePath}:{dataLineInfo.LineNumber}: data opening tag must occupy one line and use two-space indentation";
            yield break;
        }

        XElement[] children = dataElement.Elements().ToArray();
        foreach (XElement child in children)
        {
            IXmlLineInfo childLineInfo = child;
            int childLineIndex = childLineInfo.LineNumber - 1;
            string childName = child.Name.LocalName;
            bool isSupportedChild = childName is "value" or "comment";
            bool isFormatted = childLineIndex > dataLineIndex
                && childLineIndex < lines.Count
                && lines[childLineIndex].StartsWith($"    <{childName}>", StringComparison.Ordinal)
                && !lines[childLineIndex].Contains("</data>", StringComparison.Ordinal);

            if (!isSupportedChild || !isFormatted)
            {
                yield return $"{relativePath}:{childLineInfo.LineNumber}: {childName} element must occupy one line and use four-space indentation";
            }
        }

        int closingLineIndex = FindLineIndex(lines, dataLineIndex + 1, line => line == "  </data>");
        int nextDataLineIndex = FindLineIndex(
            lines,
            dataLineIndex + 1,
            line => line.StartsWith("  <data ", StringComparison.Ordinal));
        if (closingLineIndex < 0 || nextDataLineIndex >= 0 && closingLineIndex > nextDataLineIndex)
        {
            yield return $"{relativePath}:{dataLineInfo.LineNumber}: data closing tag must occupy its own line and use two-space indentation";
        }
    }

    private static int FindLineIndex(IReadOnlyList<string> lines, int startIndex, Predicate<string> predicate)
    {
        for (int index = startIndex; index < lines.Count; index++)
        {
            if (predicate(lines[index]))
            {
                return index;
            }
        }

        return -1;
    }

    [GeneratedRegex("^  <data name=\"[^\"]+\" xml:space=\"preserve\">$", RegexOptions.CultureInvariant)]
    private static partial Regex DataOpeningLineRegex();
}
