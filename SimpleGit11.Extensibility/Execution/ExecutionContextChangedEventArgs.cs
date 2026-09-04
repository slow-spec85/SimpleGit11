using System;

namespace SimpleGit11.Services.Execution;

public sealed class ExecutionContextChangedEventArgs : EventArgs
{
    public ExecutionContextChangedEventArgs(ExecutionContext previous, ExecutionContext current)
    {
        Previous = previous;
        Current = current;
    }

    public ExecutionContext Previous { get; }

    public ExecutionContext Current { get; }
}
