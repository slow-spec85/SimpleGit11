using System;

namespace SimpleGit11.Services;

internal static class GitMergeFailureDetector
{
    private const string UnrelatedHistoriesMessage = "refusing to merge unrelated histories";

    public static bool IsUnrelatedHistories(GitCommandException exception)
    {
        return exception.Message.Contains(
            UnrelatedHistoriesMessage,
            StringComparison.OrdinalIgnoreCase);
    }
}
