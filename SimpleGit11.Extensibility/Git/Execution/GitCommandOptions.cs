using System.Collections.Generic;

namespace SimpleGit11.Services.Git.Execution;

public sealed record GitCommandOptions(
    string? StandardInput = null,
    bool ThrowOnError = true,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    bool UseDefaultWorkingDirectory = false,
    GitHttpAuthentication? HttpAuthentication = null);

public sealed class GitHttpAuthentication
{
    public GitHttpAuthentication(
        string url,
        string? username = null,
        string? password = null)
    {
        Url = url;
        Username = username;
        Password = password;
    }

    public string Url { get; }

    public string? Username { get; }

    public string? Password { get; }

    public override string ToString()
    {
        return $"{nameof(GitHttpAuthentication)} {{ Url = {Url}, Username = {Username}, Password = *** }}";
    }
}
