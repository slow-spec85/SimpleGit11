using System;

namespace SimpleGit11.Services;

public class GitCommandException : Exception
{
    public GitCommandException(string message, int exitCode)
        : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}
