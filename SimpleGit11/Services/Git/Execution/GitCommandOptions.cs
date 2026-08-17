using System.Collections.Generic;

namespace SimpleGit11.Services.Git.Execution;

public sealed record GitCommandOptions(
    string? StandardInput = null,
    bool ThrowOnError = true,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null);
