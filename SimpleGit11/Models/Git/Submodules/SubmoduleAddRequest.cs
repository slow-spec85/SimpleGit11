namespace SimpleGit11.Models;

public sealed record SubmoduleAddRequest(
    string Url,
    string Path,
    string Branch);
