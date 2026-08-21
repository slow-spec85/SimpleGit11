namespace SimpleGit11.Models;

public sealed record GitCommitOptions(bool AllowEmpty = false)
{
    public static GitCommitOptions Default { get; } = new();
}
