namespace SimpleGit11.Models;

public sealed record GitPushReferenceUpdate(
    GitPushReferenceKind Kind,
    string Name,
    bool ForceWithLease = false);
