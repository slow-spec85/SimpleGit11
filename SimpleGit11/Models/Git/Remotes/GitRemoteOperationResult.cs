namespace SimpleGit11.Models;

public sealed class GitRemoteOperationResult
{
    public GitRemoteOperationResult(string output)
    {
        Output = output;
    }

    public string Output { get; }
}
