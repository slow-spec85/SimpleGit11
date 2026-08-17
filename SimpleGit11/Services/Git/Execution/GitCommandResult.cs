using System;
using System.Linq;

namespace SimpleGit11.Services.Git.Execution;

public sealed record GitCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool IsSuccess => ExitCode == 0;

    public string CombinedOutput => string.Join(
        Environment.NewLine,
        new[] { StandardOutput, StandardError }
            .Where(value => !string.IsNullOrWhiteSpace(value)))
        .Trim();
}
