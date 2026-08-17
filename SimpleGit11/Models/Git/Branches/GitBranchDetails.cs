namespace SimpleGit11.Models;

public sealed record GitBranchDetails(
    int CommitsOnlyInCurrent,
    int CommitsOnlyInSelected,
    GitCommit? MergeBaseCommit,
    bool IsMergedIntoCurrent,
    bool CanFastForwardCurrent,
    int ChangedFiles,
    DiffStat DiffStat);
