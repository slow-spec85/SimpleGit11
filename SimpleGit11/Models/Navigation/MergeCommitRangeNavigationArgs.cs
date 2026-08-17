namespace SimpleGit11.Models;

public sealed record MergeCommitRangeNavigationArgs(
    string MergeCommitShortHash,
    string FirstParentHash,
    string SecondParentHash);
