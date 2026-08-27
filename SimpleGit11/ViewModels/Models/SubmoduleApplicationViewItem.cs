using System;
using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.ViewModels;

public sealed class SubmoduleApplicationViewItem
{
    public SubmoduleApplicationViewItem(
        GitSubmoduleApplicationState state,
        ILocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(localizationService);

        State = state;
        Path = state.Path;
        string localVersion = state.IsInitialized
            ? Abbreviate(state.LocalCommit)
            : localizationService.GetString("SynchronizationSubmoduleNotInitialized");
        Description = string.Format(
            localizationService.GetString("SynchronizationSubmoduleApplicationDescription"),
            Abbreviate(state.RequiredCommit),
            localVersion);
    }

    public GitSubmoduleApplicationState State { get; }

    public string Path { get; }

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
