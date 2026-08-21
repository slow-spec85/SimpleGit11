namespace SimpleGit11.Models;

public sealed record GitCommitOperationResult(bool Completed, string Output)
{
    public static GitCommitOperationResult Canceled { get; } = new(false, "");

    public static GitCommitOperationResult Succeeded(string output) => new(true, output);
}
