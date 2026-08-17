namespace SimpleGit11.Models;

public sealed record BranchReflogDisplayItem(
    string Action,
    string Details,
    string Metadata,
    string HashTransition);
