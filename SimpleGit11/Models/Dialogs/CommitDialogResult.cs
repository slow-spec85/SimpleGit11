namespace SimpleGit11.Models;

public sealed class CommitDialogResult(string? message, bool amend)
{
    public string? Message { get; } = message;

    public bool Amend { get; } = amend;
}
