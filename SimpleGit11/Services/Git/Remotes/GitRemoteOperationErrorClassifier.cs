using System;

namespace SimpleGit11.Services;

internal static class GitRemoteOperationErrorClassifier
{
    public static GitRemoteOperationErrorKind Classify(string output)
    {
        if (output.Contains("Host key verification failed", StringComparison.OrdinalIgnoreCase)
            || output.Contains("REMOTE HOST IDENTIFICATION HAS CHANGED", StringComparison.OrdinalIgnoreCase))
        {
            return GitRemoteOperationErrorKind.HostKeyVerification;
        }

        if (output.Contains("does not support --atomic push", StringComparison.OrdinalIgnoreCase)
            || output.Contains("does not support atomic push", StringComparison.OrdinalIgnoreCase))
        {
            return GitRemoteOperationErrorKind.AtomicNotSupported;
        }

        if (output.Contains("Git Credential Manager failed", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Git Credential Manager exited", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Git Credential Manager did not return", StringComparison.OrdinalIgnoreCase))
        {
            return GitRemoteOperationErrorKind.CredentialManager;
        }

        if (output.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase)
            || output.Contains("could not read Username", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
            || output.Contains("SSH key has expired", StringComparison.OrdinalIgnoreCase)
            || output.Contains("SSH key is expired", StringComparison.OrdinalIgnoreCase)
            || output.Contains("403", StringComparison.OrdinalIgnoreCase)
            || output.Contains("401", StringComparison.OrdinalIgnoreCase))
        {
            return GitRemoteOperationErrorKind.Authentication;
        }

        if (output.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Automatic merge failed", StringComparison.OrdinalIgnoreCase)
            || output.Contains("fix conflicts", StringComparison.OrdinalIgnoreCase))
        {
            return GitRemoteOperationErrorKind.Conflict;
        }

        if (output.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase)
            || output.Contains("fetch first", StringComparison.OrdinalIgnoreCase)
            || output.Contains("rejected", StringComparison.OrdinalIgnoreCase))
        {
            return GitRemoteOperationErrorKind.NonFastForward;
        }

        return GitRemoteOperationErrorKind.General;
    }
}
