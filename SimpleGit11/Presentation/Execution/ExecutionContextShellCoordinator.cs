using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Messages;
using SimpleGit11.Services;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Presentation.Execution;

/// <summary>Coordinates shell state for all execution providers, without transport-specific UI.</summary>
internal sealed class ExecutionContextShellCoordinator : IDisposable
{
    private readonly IExecutionContextService _contexts;
    private readonly Action<Action> _dispatch;
    private readonly IAsyncCommandExecutor _executor;
    private readonly Action _resetRepository;
    private readonly Func<Task> _refreshPage;
    private readonly IMessenger _messenger;
    private readonly ILocalizationService _localization;
    private Task _refreshTask = Task.CompletedTask;
    private bool _disposed;

    public ExecutionContextShellCoordinator(
        IExecutionContextService contexts,
        Action<Action> dispatch,
        IAsyncCommandExecutor executor,
        Action resetRepository,
        Func<Task> refreshPage,
        IMessenger messenger,
        ILocalizationService localization)
    {
        _contexts = contexts;
        _dispatch = dispatch;
        _executor = executor;
        _resetRepository = resetRepository;
        _refreshPage = refreshPage;
        _messenger = messenger;
        _localization = localization;
        _contexts.CurrentChanged += Contexts_CurrentChanged;
        _contexts.ConnectionLost += Contexts_ConnectionLost;
    }

    public void Dispose()
    {
        _disposed = true;
        _contexts.CurrentChanged -= Contexts_CurrentChanged;
        _contexts.ConnectionLost -= Contexts_ConnectionLost;
    }

    private void Contexts_CurrentChanged(object? sender, ExecutionContextChangedEventArgs e)
    {
        DispatchAsync(() =>
        {
            if (_contexts.Current.Id != e.Current.Id)
            {
                return Task.CompletedTask;
            }

            _resetRepository();
            _refreshTask = _refreshPage();
            return _refreshTask;
        });
    }

    private void Contexts_ConnectionLost(object? sender, ExecutionConnectionLostEventArgs e)
    {
        DispatchAsync(async () =>
        {
            if (_contexts.Current.Id != e.Context.Id || _contexts.Current.IsLocal)
            {
                return;
            }

            await _contexts.UseLocalAsync();
            // Page refreshes can clear notifications; report the loss after the local page is ready.
            await _refreshTask;
            if (!_disposed && _contexts.Current.IsLocal)
            {
                _messenger.Send(new AppNotificationMessage(
                    this,
                    AppNotificationSeverity.Warning,
                    string.Format(_localization.GetString("ExecutionConnectionLostMessage"), e.Context.DisplayMachineName),
                    e.Exception.Message));
            }
        });
    }

    private void DispatchAsync(Func<Task> action)
    {
        if (!_disposed)
        {
            _dispatch(() =>
            {
                if (!_disposed)
                {
                    _ = _executor.ExecuteAsync(action);
                }
            });
        }
    }
}
