namespace SimpleGit11.Models;

public sealed record CommitDiffNavigationArgs(
    string Title,
    string Description,
    GitCommit? Commit);

