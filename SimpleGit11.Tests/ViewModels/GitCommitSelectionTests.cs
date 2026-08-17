using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class GitCommitSelectionTests
{
    [TestMethod]
    public void OrderOldestFirst_UsesHistoryOrderInsteadOfSelectionOrder()
    {
        GitCommit newest = CreateCommit("newest");
        GitCommit middle = CreateCommit("middle");
        GitCommit oldest = CreateCommit("oldest");

        IReadOnlyList<GitCommit> result = GitCommitSelection.OrderOldestFirst(
            [newest, middle, oldest],
            [newest, oldest, middle]);

        CollectionAssert.AreEqual(
            new[] { "oldest", "middle", "newest" },
            result.Select(commit => commit.Hash).ToArray());
    }

    [TestMethod]
    public void IsWithinScope_RightSide_RejectsSelectionContainingLeftCommit()
    {
        GitCommit right = CreateCommit("right", GitCommitRangeSide.Right);
        GitCommit left = CreateCommit("left", GitCommitRangeSide.Left);

        bool result = GitCommitSelection.IsWithinScope(
            CommitRangeCherryPickScope.RightSide,
            [right, left]);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void CanApplyTogether_RejectsMergeCommitWithOtherCommit()
    {
        GitCommit merge = new(
            "merge",
            "merge",
            "Author",
            "author@example.invalid",
            null,
            "merge",
            "merge",
            parentHashes: ["parent-1", "parent-2"]);

        Assert.IsFalse(GitCommitSelection.CanApplyTogether([CreateCommit("ordinary"), merge]));
        Assert.IsTrue(GitCommitSelection.CanApplyTogether([merge]));
    }

    private static GitCommit CreateCommit(
        string hash,
        GitCommitRangeSide rangeSide = GitCommitRangeSide.None) => new(
        hash,
        hash,
        "Author",
        "author@example.invalid",
        null,
        hash,
        hash,
        rangeSide: rangeSide);
}
