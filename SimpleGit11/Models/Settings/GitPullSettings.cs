namespace SimpleGit11.Models;

// Null means absent in the requested scope; an empty value is still configured.
public sealed record GitPullSettings(string? Rebase, string? FastForward);
