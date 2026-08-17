using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.ViewModels;

public sealed class BranchSynchronizationViewItem
{
    public BranchSynchronizationViewItem(
        BranchSynchronizationItem branch,
        ILocalizationService localizationService,
        BranchSynchronizationDirection direction)
    {
        Branch = branch;
        Name = branch.Name;
        Description = CreateDescription(branch, localizationService, direction);
    }

    public BranchSynchronizationItem Branch { get; }

    public string Name { get; }

    public string Description { get; }

    public bool CanPush => Branch.CanPush;

    public bool CanViewOutgoingCommits => Branch.CanPush;

    public bool CanViewIncomingCommits => Branch.HasIncomingCommits;

    public bool IsCurrentBranch => Branch.IsCurrent;

    public string CurrentIndicator => IsCurrentBranch ? "*" : "";

    private static string CreateDescription(
        BranchSynchronizationItem branch,
        ILocalizationService localizationService,
        BranchSynchronizationDirection direction)
    {
        if (direction == BranchSynchronizationDirection.Incoming)
        {
            if (branch.IsDiverged)
            {
                return string.Format(
                    localizationService.GetString("SynchronizationBranchDivergedDescription"),
                    PluralizationService.FormatCommitCount(branch.AheadCount, localizationService),
                    PluralizationService.FormatCommitCount(branch.BehindCount, localizationService));
            }

            return string.Format(
                localizationService.GetString("SynchronizationBranchIncomingDescription"),
                PluralizationService.FormatCommitCount(branch.BehindCount, localizationService),
                branch.RemoteTrackingBranch);
        }

        if (branch.RequiresForcePush)
        {
            return string.Format(
                localizationService.GetString("SynchronizationBranchConfiguredPushDivergedDescription"),
                PluralizationService.FormatCommitCount(branch.PushAheadCount, localizationService),
                PluralizationService.FormatCommitCount(branch.PushBehindCount, localizationService),
                branch.ConfiguredPushRemoteName);
        }

        if (branch.NeedsPublishingToPushRemote)
        {
            return string.Format(
                localizationService.GetString("SynchronizationBranchConfiguredPushUnpublishedDescription"),
                branch.ConfiguredPushRemoteName);
        }

        return string.Format(
            localizationService.GetString("SynchronizationBranchConfiguredPushOutgoingDescription"),
            PluralizationService.FormatCommitCount(branch.PushAheadCount, localizationService),
            branch.ConfiguredPushRemoteName);
    }
}

public enum BranchSynchronizationDirection
{
    Outgoing,
    Incoming
}
