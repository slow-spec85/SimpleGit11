namespace SimpleGit11.Models;

public enum GitOperationKind
{
    None,
    Merge,
    Rebase,
    CherryPick,
    Revert
}

public sealed record GitOperationState(
    GitOperationKind Kind,
    string PreparedCommitMessage = "")
{
    public static GitOperationState None { get; } = new(GitOperationKind.None);
}
