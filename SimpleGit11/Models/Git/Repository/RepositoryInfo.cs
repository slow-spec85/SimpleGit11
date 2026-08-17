namespace SimpleGit11.Models;

public sealed class RepositoryInfo
{
    public RepositoryInfo()
    {
        Path = "";
        Name = "";
        CurrentBranch = "";
        CommonGitDirectory = "";
        MainWorktreePath = "";
    }

    public RepositoryInfo(
        string path,
        string name,
        string currentBranch,
        string commonGitDirectory = "",
        string mainWorktreePath = "",
        bool isMainWorktree = true)
    {
        Path = path;
        Name = name;
        CurrentBranch = currentBranch;
        CommonGitDirectory = commonGitDirectory;
        MainWorktreePath = mainWorktreePath;
        IsMainWorktree = isMainWorktree;
    }

    public string Path { get; set; }

    public string Name { get; set; }

    public string CurrentBranch { get; set; }

    public string CommonGitDirectory { get; set; }

    public string MainWorktreePath { get; set; }

    public bool IsMainWorktree { get; set; } = true;

    public bool IsDetachedHead => CurrentBranch.StartsWith("Detached ", System.StringComparison.Ordinal);
}
