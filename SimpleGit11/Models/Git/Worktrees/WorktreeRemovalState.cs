namespace SimpleGit11.Models;

public enum WorktreeRemovalBlocker
{
    None,
    MainOrBare,
    Locked,
    Unavailable
}

public sealed record WorktreeRemovalState(
    WorktreeRemovalBlocker Blocker = WorktreeRemovalBlocker.None,
    bool HasSubmodules = false,
    bool HasChanges = false)
{
    public bool CanRemove => Blocker == WorktreeRemovalBlocker.None;

    public bool RequiresForce => CanRemove && (HasSubmodules || HasChanges);
}
