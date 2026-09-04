using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Extensions;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.Commands;

[TestClass]
public sealed class GeneratedAsyncCommandTests
{
    [TestMethod]
    public async Task Execute_OperationFails_ReportsAndPreservesFaultedExecutionTask()
    {
        InvalidOperationException expected = new("failure");
        RecordingExceptionHandler handler = new();
        FoundRepositoryViewItem item = CreateItem(
            new AsyncCommandExecutor(handler),
            _ => Task.FromException(expected));

        item.OpenCommand.TryExecute();
        Exception reported = await handler.ExceptionTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreSame(expected, reported);
        Assert.IsNotNull(item.OpenCommand.ExecutionTask);
        Assert.IsTrue(item.OpenCommand.ExecutionTask.IsFaulted);
    }

    [TestMethod]
    public async Task Execute_WhileRunning_BlocksConcurrentExecution()
    {
        TaskCompletionSource completionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        int invocationCount = 0;
        FoundRepositoryViewItem item = CreateItem(
            new AsyncCommandExecutor(new RecordingExceptionHandler()),
            async _ =>
            {
                invocationCount++;
                await completionSource.Task;
            });

        item.OpenCommand.TryExecute();
        Task firstExecution = item.OpenCommand.ExecutionTask!;
        item.OpenCommand.TryExecute();

        Assert.IsTrue(item.OpenCommand.IsRunning);
        Assert.IsFalse(item.OpenCommand.CanExecute(null));
        Assert.AreEqual(1, invocationCount);
        completionSource.SetResult();
        await firstExecution;

        Assert.IsFalse(item.OpenCommand.IsRunning);
        Assert.IsTrue(item.OpenCommand.CanExecute(null));
    }

    private static FoundRepositoryViewItem CreateItem(
        IAsyncCommandExecutor executor,
        Func<string, Task> open)
    {
        return new FoundRepositoryViewItem(
            new RepositoryInfo(@"D:\Repository", "Repository", "main"),
            executor,
            open,
            _ => { },
            _ => { });
    }

    private sealed class RecordingExceptionHandler : IAsyncCommandExceptionHandler
    {
        private readonly TaskCompletionSource<Exception> _exceptionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Exception> ExceptionTask => _exceptionSource.Task;

        public void Handle(Exception exception)
        {
            _exceptionSource.TrySetResult(exception);
        }
    }
}
