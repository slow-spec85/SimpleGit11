using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Services;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class AsyncCommandExecutorTests
{
    [TestMethod]
    public async Task ExecuteAsync_OperationFails_PropagatesAndReportsSameException()
    {
        InvalidOperationException expected = new("failure");
        RecordingExceptionHandler handler = new();
        AsyncCommandExecutor executor = new(handler);

        Task executionTask = executor.ExecuteAsync(() => Task.FromException(expected));
        InvalidOperationException actual =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => executionTask);
        Exception reported = await handler.ExceptionTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreSame(expected, actual);
        Assert.AreSame(expected, reported);
        Assert.IsTrue(executionTask.IsFaulted);
        Assert.AreEqual(1, handler.InvocationCount);
    }

    [TestMethod]
    public async Task ExecuteAsync_OperationThrowsSynchronously_ReturnsFaultedTaskAndReportsException()
    {
        InvalidOperationException expected = new("failure");
        RecordingExceptionHandler handler = new();
        AsyncCommandExecutor executor = new(handler);

        Task executionTask = executor.ExecuteAsync(() => throw expected);
        InvalidOperationException actual =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => executionTask);
        Exception reported = await handler.ExceptionTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreSame(expected, actual);
        Assert.AreSame(expected, reported);
        Assert.IsTrue(executionTask.IsFaulted);
    }

    [TestMethod]
    public async Task ExecuteAsync_ExceptionHandlerFails_DoesNotReplaceOperationException()
    {
        InvalidOperationException expected = new("operation failure");
        ThrowingExceptionHandler handler = new();
        AsyncCommandExecutor executor = new(handler);

        Task executionTask = executor.ExecuteAsync(() => Task.FromException(expected));
        InvalidOperationException actual =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => executionTask);

        Assert.AreSame(expected, actual);
        Assert.IsTrue(executionTask.IsFaulted);
    }

    private sealed class RecordingExceptionHandler : IAsyncCommandExceptionHandler
    {
        private readonly TaskCompletionSource<Exception> _exceptionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Exception> ExceptionTask => _exceptionSource.Task;

        public int InvocationCount { get; private set; }

        public void Handle(Exception exception)
        {
            InvocationCount++;
            _exceptionSource.TrySetResult(exception);
        }
    }

    private sealed class ThrowingExceptionHandler : IAsyncCommandExceptionHandler
    {
        public void Handle(Exception exception)
        {
            throw new InvalidOperationException("handler failure");
        }
    }
}
