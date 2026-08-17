using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitDiffParserTests
{
    [TestMethod]
    public void Parse_StandardDiff_ClassifiesLines()
    {
        const string diff =
            "diff --git a/file.txt b/file.txt\r\n" +
            "index 1111111..2222222 100644\r\n" +
            "--- a/file.txt\r\n" +
            "+++ b/file.txt\r\n" +
            "@@ -1,2 +1,2 @@\r\n" +
            " context\r\n" +
            "-old value\r\n" +
            "+new value";

        IReadOnlyList<DiffLine> lines = GitDiffParser.Parse(diff);

        Assert.HasCount(8, lines);
        Assert.AreEqual(DiffLineKind.Header, lines[0].Kind);
        Assert.AreEqual(DiffLineKind.Hunk, lines[4].Kind);
        Assert.AreEqual(DiffLineKind.Context, lines[5].Kind);
        Assert.AreEqual("context", lines[5].Text);
        Assert.AreEqual(DiffLineKind.Removed, lines[6].Kind);
        Assert.AreEqual("old value", lines[6].Text);
        Assert.AreEqual(DiffLineKind.Added, lines[7].Kind);
        Assert.AreEqual("new value", lines[7].Text);
    }

    [TestMethod]
    public void Parse_ChangedPair_CreatesInlineSegments()
    {
        const string diff = "@@ -1 +1 @@\n-return oldValue;\n+return newValue;";

        IReadOnlyList<DiffLine> lines = GitDiffParser.Parse(diff);
        DiffLine removedLine = lines[1];
        DiffLine addedLine = lines[2];

        Assert.HasCount(1, removedLine.InlineSegments);
        Assert.AreEqual(7, removedLine.InlineSegments[0].StartIndex);
        Assert.AreEqual(3, removedLine.InlineSegments[0].Length);
        Assert.HasCount(1, addedLine.InlineSegments);
        Assert.AreEqual(7, addedLine.InlineSegments[0].StartIndex);
        Assert.AreEqual(3, addedLine.InlineSegments[0].Length);
    }

    [TestMethod]
    public void Parse_ChangedBlock_AssignsWorkingTreeLineToAddedAndRemovedLines()
    {
        const string diff =
            "@@ -10,4 +20,4 @@\n" +
            " context before\n" +
            "-old one\n" +
            "-old two\n" +
            "+new one\n" +
            "+new two\n" +
            " context after";

        IReadOnlyList<DiffLine> lines = GitDiffParser.Parse(diff);

        Assert.AreEqual(20, lines[1].SourceLineNumber);
        Assert.AreEqual(21, lines[2].SourceLineNumber);
        Assert.AreEqual(21, lines[3].SourceLineNumber);
        Assert.AreEqual(21, lines[4].SourceLineNumber);
        Assert.AreEqual(22, lines[5].SourceLineNumber);
        Assert.AreEqual(23, lines[6].SourceLineNumber);
    }

    [TestMethod]
    public void Parse_UnicodeAndMalformedLine_PreservesTextAsContext()
    {
        const string diff = "неожиданная строка";

        IReadOnlyList<DiffLine> lines = GitDiffParser.Parse(diff);

        Assert.HasCount(1, lines);
        Assert.AreEqual(DiffLineKind.Context, lines[0].Kind);
        Assert.AreEqual(diff, lines[0].Text);
    }
}
