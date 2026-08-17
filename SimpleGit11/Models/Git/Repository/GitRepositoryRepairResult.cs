namespace SimpleGit11.Models;

public sealed class GitRepositoryRepairResult
{
    public GitRepositoryRepairResult(bool objectsFetched, string output)
    {
        ObjectsFetched = objectsFetched;
        Output = output;
    }

    public bool ObjectsFetched { get; }

    public string Output { get; }
}
