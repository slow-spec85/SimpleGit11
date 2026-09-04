using System;

namespace SimpleGit11.Services.Execution;

public sealed class ExecutionConnectionLostEventArgs(
    ExecutionContext context,
    Exception exception) : EventArgs
{
    public ExecutionContext Context { get; } = context;

    public Exception Exception { get; } = exception;
}
