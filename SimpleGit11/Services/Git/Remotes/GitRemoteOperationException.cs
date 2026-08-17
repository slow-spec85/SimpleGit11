namespace SimpleGit11.Services;

public enum GitRemoteOperationErrorKind
{
    General,
    Authentication,
    Conflict,
    NonFastForward,
    AtomicNotSupported
}

public sealed class GitRemoteOperationException : GitCommandException
{
    public GitRemoteOperationException(string message, int exitCode, GitRemoteOperationErrorKind kind)
        : base(message, exitCode)
    {
        Kind = kind;
    }

    public GitRemoteOperationErrorKind Kind { get; }
}
