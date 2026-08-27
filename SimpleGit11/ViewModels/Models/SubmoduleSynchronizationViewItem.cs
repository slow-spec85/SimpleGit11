using System;
using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.ViewModels;

public sealed class SubmoduleSynchronizationViewItem
{
    public SubmoduleSynchronizationViewItem(
        GitSubmoduleReferenceChange change,
        string branchName,
        SubmoduleSynchronizationDirection direction,
        ILocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        ArgumentNullException.ThrowIfNull(localizationService);

        Path = change.Path;
        BranchName = branchName;
        OldCommit = change.OldCommit;
        NewCommit = change.NewCommit;
        Kind = change.Kind;
        string oldVersion = Abbreviate(change.OldCommit);
        string newVersion = Abbreviate(change.NewCommit);
        Description = string.Format(
            localizationService.GetString(direction == SubmoduleSynchronizationDirection.Outgoing
                ? "SynchronizationSubmoduleOutgoingDescription"
                : "SynchronizationSubmoduleIncomingDescription"),
            oldVersion,
            newVersion,
            branchName);
    }

    public string Path { get; }

    public string BranchName { get; }

    public string OldCommit { get; }

    public string NewCommit { get; }

    public GitSubmoduleReferenceChangeKind Kind { get; }

    public string Description { get; }

    private static string Abbreviate(string commit)
    {
        if (string.IsNullOrWhiteSpace(commit))
        {
            return "—";
        }

        return commit.Length > 7 ? commit[..7] : commit;
    }
}

public enum SubmoduleSynchronizationDirection
{
    Outgoing,
    Incoming
}
