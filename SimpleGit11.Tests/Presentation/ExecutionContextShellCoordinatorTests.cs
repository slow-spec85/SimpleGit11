using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Messages;
using SimpleGit11.Presentation.Execution;
using SimpleGit11.Services;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class ExecutionContextShellCoordinatorTests
{
    private readonly ConnectionTestContexts _contexts = new();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly List<string> _calls = [];
    private readonly RecordingExecutor _executor = new();

    private ExecutionContextShellCoordinator Create(Action<Action>? dispatch = null, Func<Task>? refresh = null) => new(
        _contexts, dispatch ?? (action => action()), _executor,
        () => _calls.Add("reset"), refresh ?? (() => { _calls.Add("refresh"); return Task.CompletedTask; }),
        _messenger, new ConnectionTestLocalization());

    [TestMethod]
    public async Task ContextChanged_AnyProvider_ResetsRepositoryBeforeRefreshingPage()
    {
        using ExecutionContextShellCoordinator coordinator = Create();

        _contexts.Switch(false, "other.provider");
        await _executor.CompleteAsync();

        CollectionAssert.AreEqual(new[] { "reset", "refresh" }, _calls);
    }

    [TestMethod]
    public async Task ContextChanged_BackgroundNotification_DispatchesAndAwaitsPageRefresh()
    {
        Queue<Action> pending = new();
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ExecutionContextShellCoordinator coordinator = Create(pending.Enqueue, () => completion.Task);

        await Task.Run(() => _contexts.Switch(false));
        Assert.IsEmpty(_calls);
        pending.Dequeue()();
        CollectionAssert.AreEqual(new[] { "reset" }, _calls);
        Assert.IsFalse(_executor.Tasks.Single().IsCompleted);
        completion.SetResult();
        await _executor.CompleteAsync();
    }

    [TestMethod]
    public async Task ContextChanged_StaleQueuedNotification_IsIgnored()
    {
        Queue<Action> pending = new();
        using ExecutionContextShellCoordinator coordinator = Create(pending.Enqueue);
        _contexts.Switch(false);
        _contexts.Switch(true);

        pending.Dequeue()();
        Assert.IsEmpty(_calls);
        pending.Dequeue()();
        await _executor.CompleteAsync();

        CollectionAssert.AreEqual(new[] { "reset", "refresh" }, _calls);
    }

    [TestMethod]
    public async Task ConnectionLost_CurrentRemote_ReturnsLocalAndNotifiesOnce()
    {
        _contexts.Switch(false, "other.provider");
        using ExecutionContextShellCoordinator coordinator = Create();
        List<AppNotificationMessage> notifications = [];
        _messenger.Register<AppNotificationMessage>(notifications, (recipient, message) =>
            ((List<AppNotificationMessage>)recipient).Add(message));
        var lost = _contexts.Current;

        _contexts.Lose(lost);
        _contexts.Lose(lost);
        await _executor.CompleteAsync();

        Assert.IsTrue(_contexts.Current.IsLocal);
        Assert.AreEqual(1, _contexts.UseLocalCalls);
        CollectionAssert.AreEqual(new[] { "reset", "refresh" }, _calls);
        AppNotificationMessage notification = notifications.Single();
        Assert.AreEqual(AppNotificationSeverity.Warning, notification.Severity);
        StringAssert.Contains(notification.Message, "test-server");
        Assert.AreEqual("Connection lost", notification.Details);
    }

    [TestMethod]
    public async Task ConnectionLost_WaitsForPageRefreshBeforeWarning()
    {
        _contexts.Switch(false);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ExecutionContextShellCoordinator coordinator = Create(refresh: () => completion.Task);
        List<AppNotificationMessage> notifications = [];
        _messenger.Register<AppNotificationMessage>(notifications, (recipient, message) =>
            ((List<AppNotificationMessage>)recipient).Add(message));

        _contexts.Lose(_contexts.Current);
        Assert.IsEmpty(notifications);
        completion.SetResult();
        await _executor.CompleteAsync();

        Assert.HasCount(1, notifications);
    }

    [TestMethod]
    public async Task ConnectionLost_QueuedAfterSwitch_DoesNotDisconnectNewContext()
    {
        _contexts.Switch(false);
        Queue<Action> pending = new();
        using ExecutionContextShellCoordinator coordinator = Create(pending.Enqueue);
        _contexts.Lose(_contexts.Current);
        _contexts.Switch(false, "new.provider");

        while (pending.TryDequeue(out Action? action))
        {
            action();
        }
        await _executor.CompleteAsync();

        Assert.AreEqual("new.provider", _contexts.Current.ProviderId);
        Assert.AreEqual(0, _contexts.UseLocalCalls);
    }

    [TestMethod]
    public async Task ConnectionLost_LocalContext_IsIgnored()
    {
        using ExecutionContextShellCoordinator coordinator = Create();
        _contexts.Lose(_contexts.Current);
        await _executor.CompleteAsync();

        Assert.AreEqual(0, _contexts.UseLocalCalls);
        Assert.IsEmpty(_calls);
    }

    [TestMethod]
    public async Task Dispose_UnsubscribesAndIgnoresQueuedCallbacks()
    {
        Queue<Action> pending = new();
        ExecutionContextShellCoordinator coordinator = Create(pending.Enqueue);
        _contexts.Switch(false);
        _contexts.Lose(_contexts.Current);
        coordinator.Dispose();

        while (pending.TryDequeue(out Action? action))
        {
            action();
        }
        _contexts.Switch(true);
        _contexts.Lose(_contexts.Current);
        await _executor.CompleteAsync();

        Assert.IsEmpty(pending);
        Assert.IsEmpty(_calls);
        Assert.AreEqual(0, _contexts.UseLocalCalls);
    }

    [TestMethod]
    public async Task RefreshFailure_IsObservedByCommandExecutor()
    {
        InvalidOperationException failure = new("Refresh failed");
        using ExecutionContextShellCoordinator coordinator = Create(refresh: () => Task.FromException(failure));
        _contexts.Switch(false);

        Exception actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(_executor.CompleteAsync);

        Assert.AreSame(failure, actual);
    }

    private sealed class RecordingExecutor : IAsyncCommandExecutor
    {
        public List<Task> Tasks { get; } = [];
        public Task ExecuteAsync(Func<Task> operation)
        {
            Task task = operation();
            Tasks.Add(task);
            return task;
        }
        public Task CompleteAsync() => Task.WhenAll(Tasks);
    }
}
