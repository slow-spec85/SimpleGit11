using System;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Services;

public sealed class GitOperationQueue : IGitOperationQueue
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public bool IsRunning { get; private set; }

    public async Task EnqueueAsync(Func<Task> operation)
    {
        await _semaphore.WaitAsync();
        try
        {
            IsRunning = true;
            await operation();
        }
        finally
        {
            IsRunning = false;
            _semaphore.Release();
        }
    }
}
