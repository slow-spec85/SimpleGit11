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

    private static BranchSynchronizationItem CreateBranch(
        string pushTrackingState,
        int pushAheadCount,
        int pushBehindCount)
    {
        return new BranchSynchronizationItem(
            name: "main",
            isCurrent: true,
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
            behindCount: 0);
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
