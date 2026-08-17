namespace SimpleGit11.Models;

public enum GitRevisionKind
{
    Head,
    Branch,
    Tag,
    Commit
}

public sealed record GitRevisionSuggestion(
    string Value,
    string DisplayName,
    string Description,
    string ShortHash,
    bool IsRemote = false);

public sealed record GitResolvedRevision(
    string CommitHash,
    string ShortHash);
