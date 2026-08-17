namespace SimpleGit11.Models;

public sealed record RevisionRangeNavigationArgs(
    string Title,
    string Description,
    string EmptyMessage,
    string RevisionRange,
    string LeftRevision = "",
    string RightRevision = "",
    string LeftLabel = "",
    string RightLabel = "",
    CommitRangeCherryPickScope CherryPickScope = CommitRangeCherryPickScope.None)
{
    public bool IsTwoSidedComparison =>
        !string.IsNullOrWhiteSpace(LeftRevision) &&
        !string.IsNullOrWhiteSpace(RightRevision);
}
