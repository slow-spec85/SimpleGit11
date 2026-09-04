using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class BranchSynchronizationViewItemTests
{
    [TestMethod]
    public void Constructor_OutgoingBranch_IncludesCommitCountAndRepository()
    {
        BranchSynchronizationItem branch = CreateBranch(
            pushTrackingState: ">",
            pushAheadCount: 3,
            pushBehindCount: 0);

        BranchSynchronizationViewItem item = new(
            branch,
            new TestLocalizationService(),
            BranchSynchronizationDirection.Outgoing);

        Assert.AreEqual(
            "Outgoing: 3 commits. Repository: “public”.",
            item.Description);
    }

    [TestMethod]
    public void Constructor_DivergedPushBranch_IncludesBothCommitCountsAndRepository()
    {
        BranchSynchronizationItem branch = CreateBranch(
            pushTrackingState: "<>",
            pushAheadCount: 2,
            pushBehindCount: 4);

        BranchSynchronizationViewItem item = new(
            branch,
            new TestLocalizationService(),
            BranchSynchronizationDirection.Outgoing);

        Assert.AreEqual(
            "Diverged: 2 commits outgoing and 4 commits incoming. Repository: “public”.",
            item.Description);
    }

    [TestMethod]
    [DataRow(true, true, false)]
    [DataRow(false, false, true)]
    public void Constructor_ExposesMutuallyExclusiveCurrentBranchFlags(
        bool isCurrent,
        bool expectedCurrent,
        bool expectedNotCurrent)
    {
        BranchSynchronizationItem branch = CreateBranch(
            pushTrackingState: ">",
            pushAheadCount: 1,
            pushBehindCount: 0,
            isCurrent: isCurrent);

        BranchSynchronizationViewItem item = new(
            branch,
            new TestLocalizationService(),
            BranchSynchronizationDirection.Outgoing);

        Assert.AreEqual(expectedCurrent, item.IsCurrentBranch);
        Assert.AreEqual(expectedNotCurrent, item.IsNotCurrentBranch);
    }

    [TestMethod]
    [DataRow(false, "", 1, true)]
    [DataRow(false, "C:/other worktree", 1, false)]
    [DataRow(true, "C:/current", 1, false)]
    [DataRow(false, "", 0, false)]
    public void CanSwitchAndPull_RequiresIncomingBranchAvailableInThisWorktree(
        bool isCurrent,
        string worktreePath,
        int behindCount,
        bool expected)
    {
        BranchSynchronizationViewItem item = new(
            CreateBranch("=", 0, 0, isCurrent, worktreePath, behindCount),
            new TestLocalizationService(),
            BranchSynchronizationDirection.Incoming);

        Assert.AreEqual(expected, item.CanSwitchAndPull);
        Assert.AreEqual(behindCount > 0, item.CanViewIncomingCommits);
        const string otherWorktreeDescription =
            "Switching is unavailable: this branch is checked out in another worktree";
        if (!isCurrent && worktreePath.Length > 0)
        {
            StringAssert.Contains(item.Description, otherWorktreeDescription);
        }
        else
        {
            Assert.IsFalse(item.Description.Contains(otherWorktreeDescription));
        }
    }

    private static BranchSynchronizationItem CreateBranch(
        string pushTrackingState,
        int pushAheadCount,
        int pushBehindCount,
        bool isCurrent = true,
        string worktreePath = "",
        int behindCount = 0)
    {
        return new BranchSynchronizationItem(
            name: "main",
            isCurrent: isCurrent,
            upstreamBranch: "origin/main",
            upstreamRemoteName: "origin",
            upstreamTrackingState: "=",
            pushRemoteName: "public",
            explicitPushRemoteName: "public",
            pushTrackingBranch: "public/main",
            pushTrackingState: pushTrackingState,
            pushAheadCount: pushAheadCount,
            pushBehindCount: pushBehindCount,
            hasPushRemoteOverride: true,
            remoteTrackingBranch: "origin/main",
            tracksSelectedRemote: true,
            isPublishedToRemote: true,
            aheadCount: 0,
            behindCount: behindCount,
            worktreePath: worktreePath);
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public AppLanguage CurrentLanguage => AppLanguage.English;

        public string GetString(string resourceKey)
        {
            return resourceKey switch
            {
                "SynchronizationBranchConfiguredPushOutgoingDescription" =>
                    "Outgoing: {0}. Repository: “{1}”.",
                "SynchronizationBranchConfiguredPushDivergedDescription" =>
                    "Diverged: {0} outgoing and {1} incoming. Repository: “{2}”.",
                "SynchronizationBranchIncomingDescription" => "Incoming: {0} from {1}.",
                "SynchronizationBranchInOtherWorktreeDescription" =>
                    "Switching is unavailable: this branch is checked out in another worktree",
                "CommitCountOne" => "commit",
                "CommitCountMany" => "commits",
                _ => resourceKey
            };
        }

        public void ApplyLanguage()
        {
        }

        public void SetLanguage(AppLanguage language)
        {
        }
    }
}
