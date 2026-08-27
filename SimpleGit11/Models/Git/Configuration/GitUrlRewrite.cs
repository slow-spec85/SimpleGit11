namespace SimpleGit11.Models;

public sealed record GitUrlRewrite(
    string InsteadOfUrl,
    string ReplacementUrl);
