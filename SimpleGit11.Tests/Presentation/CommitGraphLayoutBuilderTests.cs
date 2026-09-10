using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Presentation.Commits;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class CommitGraphLayoutBuilderTests
{
    [TestMethod]
    public void Build_LinearHistory_UsesOneStableLane()
    {
        IReadOnlyList<GitCommit> commits =
        [
            CreateCommit("c", "b"),
            CreateCommit("b", "a"),
            CreateCommit("a")
        ];

        CommitGraphLayout layout = CommitGraphLayoutBuilder.Build(commits);

        Assert.AreEqual(1, layout.LaneCount);
        Assert.IsTrue(layout.Rows.Values.All(row => row.NodeLane == 0 && row.LaneCount == 1));
        Assert.HasCount(1, layout.Rows["c"].Segments);
        Assert.AreEqual(CommitGraphEndpoint.Node, layout.Rows["c"].Segments[0].Start);
        Assert.AreEqual(CommitGraphEndpoint.Bottom, layout.Rows["c"].Segments[0].End);
    }

    [TestMethod]
    public void Build_MergeHistory_CreatesSecondLaneAndJoinsItAgain()
    {
        IReadOnlyList<GitCommit> commits =
        [
            CreateCommit("merge", "main", "feature"),
            CreateCommit("feature", "root"),
            CreateCommit("main", "root"),
            CreateCommit("root")
        ];

        CommitGraphLayout layout = CommitGraphLayoutBuilder.Build(commits);

        Assert.AreEqual(2, layout.LaneCount);
        CommitGraphRow merge = layout.Rows["merge"];
        Assert.AreEqual(0, merge.NodeLane);
        Assert.IsTrue(merge.Segments.Any(segment =>
            segment.Start == CommitGraphEndpoint.Node
            && segment.End == CommitGraphEndpoint.Bottom
            && segment.EndLane == 1));
        Assert.AreEqual(1, layout.Rows["feature"].NodeLane);
        Assert.AreEqual(0, layout.Rows["root"].NodeLane);
    }

    [TestMethod]
    public void Build_AppendedPage_DoesNotChangeExistingRows()
    {
        IReadOnlyList<GitCommit> firstPage =
        [
            CreateCommit("merge", "main", "feature"),
            CreateCommit("feature", "root")
        ];
        IReadOnlyList<GitCommit> allCommits =
        [
            .. firstPage,
            CreateCommit("main", "root"),
            CreateCommit("root")
        ];

        CommitGraphLayout firstLayout = CommitGraphLayoutBuilder.Build(firstPage);
        CommitGraphLayout completedLayout = CommitGraphLayoutBuilder.Build(allCommits);

        AssertRowsEqual(firstLayout.Rows["merge"], completedLayout.Rows["merge"]);
        AssertRowsEqual(firstLayout.Rows["feature"], completedLayout.Rows["feature"]);
    }

    [TestMethod]
    public void Build_FilteredHistory_ConnectsNearestVisibleAncestorWithCollapsedSegment()
    {
        IReadOnlyList<GitCommit> commits =
        [
            CreateCommit("head", "hidden-1"),
            CreateCommit("hidden-1", "hidden-2"),
            CreateCommit("hidden-2", "root"),
            CreateCommit("root")
        ];
        HashSet<string> visible = new(StringComparer.OrdinalIgnoreCase) { "head", "root" };

        CommitGraphLayout layout = CommitGraphLayoutBuilder.Build(commits, visible);

        CommitGraphSegment outgoing = layout.Rows["head"].Segments.Single(segment =>
            segment.Start == CommitGraphEndpoint.Node);
        Assert.IsTrue(outgoing.IsCollapsed);
        Assert.IsTrue(layout.Rows["root"].Segments.Any(segment =>
            segment.End == CommitGraphEndpoint.Node
            && segment.IsCollapsed));
    }

    [TestMethod]
    public void Build_MainlineOnly_IgnoresMergedParentPath()
    {
        IReadOnlyList<GitCommit> commits =
        [
            CreateCommit("merge", "main", "feature"),
            CreateCommit("feature", "root"),
            CreateCommit("main", "root"),
            CreateCommit("root")
        ];
        HashSet<string> visible = new(StringComparer.OrdinalIgnoreCase) { "merge", "main", "root" };

        CommitGraphLayout layout = CommitGraphLayoutBuilder.Build(commits, visible, firstParentOnly: true);

        Assert.AreEqual(1, layout.LaneCount);
        Assert.IsFalse(layout.Rows.Values.SelectMany(row => row.Segments).Any(segment => segment.IsCollapsed));
    }

    [TestMethod]
    public void Build_OctopusMerge_AssignsOneLanePerParent()
    {
        IReadOnlyList<GitCommit> commits =
        [
            CreateCommit("merge", "main", "feature-a", "feature-b"),
            CreateCommit("feature-a", "root"),
            CreateCommit("feature-b", "root"),
            CreateCommit("main", "root"),
            CreateCommit("root")
        ];

        CommitGraphLayout layout = CommitGraphLayoutBuilder.Build(commits);

        Assert.AreEqual(3, layout.LaneCount);
        Assert.AreEqual(3, layout.Rows["merge"].Segments.Count(segment =>
            segment.Start == CommitGraphEndpoint.Node
            && segment.End == CommitGraphEndpoint.Bottom));
    }

    [TestMethod]
    public void Build_FilteredHiddenMerge_PreservesBothVisibleAncestorPaths()
    {
        IReadOnlyList<GitCommit> commits =
        [
            CreateCommit("head", "hidden-merge"),
            CreateCommit("hidden-merge", "main", "feature"),
            CreateCommit("feature", "root"),
            CreateCommit("main", "root"),
            CreateCommit("root")
        ];
        HashSet<string> visible = new(StringComparer.OrdinalIgnoreCase) { "head", "feature", "main", "root" };

        CommitGraphLayout layout = CommitGraphLayoutBuilder.Build(commits, visible);

        Assert.AreEqual(2, layout.LaneCount);
        Assert.AreEqual(2, layout.Rows["head"].Segments.Count(segment =>
            segment.Start == CommitGraphEndpoint.Node
            && segment.IsCollapsed));
    }

    [TestMethod]
    public void Build_UnknownParent_ContinuesBeyondLoadedPage()
    {
        IReadOnlyList<GitCommit> commits = [CreateCommit("head", "not-loaded")];

        CommitGraphLayout layout = CommitGraphLayoutBuilder.Build(commits);

        CommitGraphSegment continuation = layout.Rows["head"].Segments.Single();
        Assert.AreEqual(CommitGraphEndpoint.Node, continuation.Start);
        Assert.AreEqual(CommitGraphEndpoint.Bottom, continuation.End);
        Assert.IsFalse(continuation.IsCollapsed);
    }

    private static GitCommit CreateCommit(string hash, params string[] parents) => new(
        hash,
        hash,
        "Author",
        "author@example.invalid",
        null,
        hash,
        hash,
        parentHashes: parents);

    private static void AssertRowsEqual(CommitGraphRow expected, CommitGraphRow actual)
    {
        Assert.AreEqual(expected.NodeLane, actual.NodeLane);
        Assert.AreEqual(expected.NodeColorIndex, actual.NodeColorIndex);
        Assert.AreEqual(expected.LaneCount, actual.LaneCount);
        CollectionAssert.AreEqual(expected.Segments.ToArray(), actual.Segments.ToArray());
    }
}
