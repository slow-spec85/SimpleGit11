namespace SimpleGit11.Models;

public sealed record GitSubmoduleApplicationState(
    string Path,
    string OwnerRepositoryPath,
    string RelativePath,
    string RequiredCommit,
    string LocalCommit,
    bool IsInitialized)
{
    public bool RequiresApplication => !IsInitialized
        || !string.Equals(RequiredCommit, LocalCommit, System.StringComparison.OrdinalIgnoreCase);
}
