namespace SimpleGit11.Models;

public sealed record GitSubmoduleReferenceChange(
    string Path,
    string OldCommit,
    string NewCommit,
    GitSubmoduleReferenceChangeKind Kind);
