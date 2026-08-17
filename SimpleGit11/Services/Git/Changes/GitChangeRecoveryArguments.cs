using System;
using System.Collections.Generic;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

internal static class GitChangeRecoveryArguments
{
    public static IReadOnlyList<IReadOnlyList<string>> CreateDiscardFileCommands(
        GitChangedFile statusEntry)
    {
        ArgumentNullException.ThrowIfNull(statusEntry);

        if (statusEntry.Status == "Untracked")
        {
            return [["clean", "-f", "--", statusEntry.Path]];
        }

        if (statusEntry.Status == "Added")
        {
            return
            [
                ["restore", "--staged", "--", statusEntry.Path],
                ["clean", "-f", "--", statusEntry.Path]
            ];
        }

        return [["restore", "--staged", "--worktree", "--", statusEntry.Path]];
    }

    public static IReadOnlyList<string> CreateResetArguments(string commitHash, string mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitHash);

        string resetMode = mode switch
        {
            "soft" => "--soft",
            "mixed" => "--mixed",
            "hard" => "--hard",
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unsupported reset mode.")
        };

        return ["reset", resetMode, commitHash];
    }
}
