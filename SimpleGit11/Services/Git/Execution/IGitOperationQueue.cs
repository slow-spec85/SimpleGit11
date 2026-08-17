using System;
using System.Threading.Tasks;

namespace SimpleGit11.Services;

public interface IGitOperationQueue
{
    bool IsRunning { get; }

    Task EnqueueAsync(Func<Task> operation);
}
