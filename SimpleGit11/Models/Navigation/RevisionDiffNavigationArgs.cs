namespace SimpleGit11.Models;

public sealed record RevisionDiffNavigationArgs(
    string Title,
    string Description,
    string OldRevision,
    string NewRevision);
