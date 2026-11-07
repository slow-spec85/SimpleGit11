using System;
using System.Threading.Tasks;

namespace SimpleGit11.Services;

public interface IGitOperationQueue
{
    event EventHandler? WorkingTreeMayHaveChanged;

    bool IsRunning { get; }

    Task EnqueueAsync(Func<Task> operation, bool mayChangeWorkingTree = false);
}
