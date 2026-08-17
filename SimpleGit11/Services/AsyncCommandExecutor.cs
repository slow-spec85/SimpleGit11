using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SimpleGit11.Services;

public sealed class AsyncCommandExecutor(IAsyncCommandExceptionHandler exceptionHandler)
    : IAsyncCommandExecutor
{
    private readonly IAsyncCommandExceptionHandler _exceptionHandler =
        exceptionHandler ?? throw new ArgumentNullException(nameof(exceptionHandler));

    public Task ExecuteAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        Task executionTask;
        try
        {
            executionTask = operation();
        }
        catch (Exception exception)
        {
            executionTask = Task.FromException(exception);
        }

        _ = ObserveAsync(executionTask);
        return executionTask;
    }

    private async Task ObserveAsync(Task executionTask)
    {
        try
        {
            await executionTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            try
            {
                _exceptionHandler.Handle(exception);
            }
            catch (Exception handlerException)
            {
                Debug.WriteLine(handlerException);
            }
        }
    }
}
