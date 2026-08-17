namespace SimpleGit11.Models;

public sealed record GitCherryPickOptions(
    bool AppendSourceReference = false,
    bool AddSignOff = false,
    bool NoCommit = false,
    int? MainlineParentNumber = null)
{
    public static GitCherryPickOptions Default { get; } = new();
}
