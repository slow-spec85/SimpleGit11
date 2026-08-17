namespace SimpleGit11.Models;

public sealed record GitTagDetails(
    GitCommit? TargetCommit,
    string TargetObjectType,
    string TaggerName,
    string TaggerEmail,
    string TaggerDate,
    string Message);
