namespace SimpleGit11.Models;

public enum WorktreeCreationMode
{
    ExistingBranch,
    NewBranch,
    Detached
}

public sealed record WorktreeCreationRequest(
    string Path,
    string StartPoint,
    string NewBranchName,
    bool IsDetached,
    bool IsLocked,
    WorktreeCreationMode CreationMode);
