namespace SimpleGit11.Services;

public enum GitRemoteOperationErrorKind
{
    General,
    HostKeyVerification,
    Authentication,
    CredentialManager,
    Conflict,
    NonFastForward,
    AtomicNotSupported
}

public sealed class GitRemoteOperationException : GitCommandException
{
    public GitRemoteOperationException(
        string message,
        int exitCode,
        GitRemoteOperationErrorKind kind,
        string? executionMachineName = null)
        : base(message, exitCode)
    {
        Kind = kind;
        ExecutionMachineName = executionMachineName;
    }

    public GitRemoteOperationErrorKind Kind { get; }

    public string? ExecutionMachineName { get; }
}
