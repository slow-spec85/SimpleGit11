using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class DiffTextFormatterTests
{
    [TestMethod]
    public void FormatEditableFile_RemovesDeletedRowsAndKeepsAddedHighlights()
    {
        DiffLine addedLine = new(
            "new",
            DiffLineKind.Added,
            "+",
            inlineSegments: [new DiffLineSegment(0, 3)]);
        IReadOnlyList<DiffLine> diffLines =
        [
            new("@@ -1 +1 @@", DiffLineKind.Hunk),
            new("old", DiffLineKind.Removed, "-"),
            addedLine
        ];

        IReadOnlyList<DiffLine> result = DiffTextFormatter.FormatEditableFile("new", diffLines);

        Assert.HasCount(1, result);
        Assert.AreEqual("new", result[0].Text);
        Assert.AreEqual(DiffLineKind.Added, result[0].Kind);
        CollectionAssert.AreEqual(addedLine.InlineSegments.ToArray(), result[0].InlineSegments.ToArray());
    }

    [TestMethod]
    public void FormatFullFile_NewFileView_InsertsRemovedLineBeforeReplacement()
    {
        IReadOnlyList<DiffLine> diffLines =
        [
            new("@@ -1,3 +1,3 @@", DiffLineKind.Hunk),
            new("one", DiffLineKind.Context),
            new("old", DiffLineKind.Removed, "-"),
            new("new", DiffLineKind.Added, "+"),
            new("three", DiffLineKind.Context)
        ];

        IReadOnlyList<DiffLine> result =
            DiffTextFormatter.FormatFullFile("one\nnew\nthree", diffLines, useOldLineNumbers: false);

        CollectionAssert.AreEqual(
            new[] { "one", "old", "new", "three" },
            result.Select(line => line.Text).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                DiffLineKind.Context,
                DiffLineKind.Removed,
                DiffLineKind.Added,
                DiffLineKind.Context
            },
            result.Select(line => line.Kind).ToArray());
        CollectionAssert.AreEqual(
            new[] { "1", "", "2", "3" },
            result.Select(line => line.LineNumberText).ToArray());
    }

    [TestMethod]
    public void FormatFullFile_OldFileView_MarksRemovedLine()
    {
        IReadOnlyList<DiffLine> diffLines =
        [
            new("@@ -1,3 +1,3 @@", DiffLineKind.Hunk),
            new("one", DiffLineKind.Context),
            new("old", DiffLineKind.Removed, "-"),
            new("new", DiffLineKind.Added, "+"),
            new("three", DiffLineKind.Context)
        ];

        IReadOnlyList<DiffLine> result =
            DiffTextFormatter.FormatFullFile("one\nold\nthree", diffLines, useOldLineNumbers: true);

        Assert.AreEqual(DiffLineKind.Removed, result[1].Kind);
        Assert.AreEqual("old", result[1].Text);
        CollectionAssert.AreEqual(
            new[] { "1", "", "3" },
            result.Select(line => line.LineNumberText).ToArray());
    }

    [TestMethod]
    public void FormatFullFile_DeletedFile_MarksEveryLineAsRemoved()
    {
        IReadOnlyList<DiffLine> diffLines =
        [
            new("@@ -1,3 +0,0 @@", DiffLineKind.Hunk),
            new("one", DiffLineKind.Removed, "-"),
            new("two", DiffLineKind.Removed, "-"),
            new("three", DiffLineKind.Removed, "-")
        ];

        IReadOnlyList<DiffLine> result =
            DiffTextFormatter.FormatFullFile("one\ntwo\nthree", diffLines, useOldLineNumbers: true);

        CollectionAssert.AreEqual(
            new[] { DiffLineKind.Removed, DiffLineKind.Removed, DiffLineKind.Removed },
            result.Select(line => line.Kind).ToArray());
        CollectionAssert.AreEqual(
            new[] { "-", "-", "-" },
            result.Select(line => line.Marker).ToArray());
        CollectionAssert.AreEqual(
            new[] { "", "", "" },
            result.Select(line => line.LineNumberText).ToArray());
    }

    [TestMethod]
    public void FormatFullFile_ConflictContent_MarksConflictLines()
    {
        IReadOnlyList<DiffLine> diffLines =
        [
            new("<<<<<<< HEAD", DiffLineKind.ConflictMarker)
        ];
        const string content = "<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> branch";

        IReadOnlyList<DiffLine> result =
            DiffTextFormatter.FormatFullFile(content, diffLines, useOldLineNumbers: false);

        Assert.HasCount(5, result);
        Assert.AreEqual(DiffLineKind.ConflictMarker, result[0].Kind);
        Assert.AreEqual(DiffLineKind.Context, result[1].Kind);
        Assert.AreEqual(DiffLineKind.ConflictMarker, result[2].Kind);
        Assert.AreEqual(DiffLineKind.ConflictMarker, result[4].Kind);
    }
}
