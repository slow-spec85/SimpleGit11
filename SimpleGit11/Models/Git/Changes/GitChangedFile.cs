namespace SimpleGit11.Models;

public sealed class GitChangedFile
{
    public GitChangedFile(
        string path,
        string status,
        DiffStat? stat = null,
        GitChangeState? state = null)
    {
        Path = path;
        Status = status;
        Stat = stat ?? DiffStat.Empty;
        State = state;
    }

    public string Path { get; }

    public string FileName
    {
        get
        {
            string normalizedPath = Path.Replace('\\', '/');
            int separatorIndex = normalizedPath.LastIndexOf('/');
            return separatorIndex >= 0 ? normalizedPath[(separatorIndex + 1)..] : normalizedPath;
        }
    }

    public string DirectoryPath
    {
        get
        {
            string normalizedPath = Path.Replace('\\', '/');
            int separatorIndex = normalizedPath.LastIndexOf('/');
            return separatorIndex > 0 ? normalizedPath[..separatorIndex] : ".";
        }
    }

    public string Status { get; }

    public DiffStat Stat { get; }

    public string StatusIndicator => Status switch
    {
        "Added" => "A",
        "Deleted" => "D",
        "Modified" => "M",
        "Renamed" => "R",
        "Copied" => "C",
        "Conflict" => "!",
        "Untracked" => "?",
        _ => "*"
    };

    public GitChangeState? State { get; }

    public bool IsStaged => State == GitChangeState.Staged;

    public bool IsUnstaged => State == GitChangeState.Unstaged;

    public bool IsConflicted => State == GitChangeState.Conflicted;

    public bool IsNotConflicted => !IsConflicted;

    public bool CanDiscard => State.HasValue && !IsConflicted;

    public string StateLabel => State switch
    {
        GitChangeState.Staged => "staged",
        GitChangeState.Conflicted => "conflicted",
        _ => ""
    };

    public string Tooltip => $"{Status} - {Path}";
}
