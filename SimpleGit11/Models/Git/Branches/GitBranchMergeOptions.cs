namespace SimpleGit11.Models;

public sealed record GitBranchMergeOptions(
    bool Squash = false,
    bool NoCommit = false,
    bool AllowUnrelatedHistories = false);
