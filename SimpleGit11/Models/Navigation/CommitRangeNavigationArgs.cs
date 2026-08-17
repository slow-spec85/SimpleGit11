namespace SimpleGit11.Models;

public sealed record CommitRangeNavigationArgs(
    CommitRangeDirection Direction,
    GitRemote Remote,
    BranchSynchronizationItem Branch)
{
    public CommitRangeCherryPickScope CherryPickScope => Direction == CommitRangeDirection.Incoming
        ? CommitRangeCherryPickScope.AllCommits
        : CommitRangeCherryPickScope.None;
}
