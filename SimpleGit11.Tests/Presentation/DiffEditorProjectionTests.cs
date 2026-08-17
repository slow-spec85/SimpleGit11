using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Presentation.Editor;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class DiffEditorProjectionTests
{
    [TestMethod]
    public void Create_MergesAdjacentBackgroundsAndClampsInlineRanges()
    {
        IReadOnlyList<DiffLine> lines =
        [
            new("one", DiffLineKind.Context),
            new("added", DiffLineKind.Added, inlineSegments: [new DiffLineSegment(1, 50)]),
            new("again", DiffLineKind.Added),
            new("removed", DiffLineKind.Removed),
            new("last", DiffLineKind.Context)
        ];

        DiffEditorProjection projection = DiffEditorProjection.Create(lines);

        CollectionAssert.AreEqual(lines.Select(line => line.Text).ToArray(), projection.Lines.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                new DiffEditorLineBlock(1, 2, DiffLineKind.Added),
                new DiffEditorLineBlock(3, 3, DiffLineKind.Removed)
            },
            projection.LineBlocks.ToArray());
        CollectionAssert.AreEqual(
            new[] { new DiffEditorTextRange(1, 1, 4, DiffLineKind.Added) },
            projection.TextRanges.ToArray());
    }

}
