using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Services.Execution;

public sealed class ExecutionContextService : IExecutionContextService, IAsyncDisposable
{
    private readonly IExecutionProviderRegistry _providerRegistry;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private long _version = 1;

    public ExecutionContextService(
        IExecutionProviderRegistry providerRegistry,
        Local.LocalExecutionRuntime localRuntime)
    {
        _providerRegistry = providerRegistry;
        Current = new ExecutionContext(
            Guid.NewGuid(),
            _version,
            BuiltInExecutionProviderIds.Local,
            null,
            localRuntime);
        SubscribeToConnectionLoss(localRuntime);
    }

    public ExecutionContext Current { get; private set; }

    public event EventHandler<ExecutionContextChangedEventArgs>? CurrentChanged;

    public event EventHandler<ExecutionConnectionLostEventArgs>? ConnectionLost;

    public async Task ActivateAsync(
        string providerId,
        ExecutionConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentNullException.ThrowIfNull(request);
        await _switchLock.WaitAsync(cancellationToken);
        try
        {
            IExecutionProvider provider = _providerRegistry.GetRequiredProvider(providerId);
            IExecutionRuntime runtime = await provider.ConnectAsync(request, cancellationToken);
            ExecutionContext next = new(
                Guid.NewGuid(),
                Interlocked.Increment(ref _version),
                provider.Id,
                request.ProfileId,
                runtime);
            await ReplaceAsync(next);
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public async Task UseLocalAsync(CancellationToken cancellationToken = default)
    {
        await _switchLock.WaitAsync(cancellationToken);
        try
        {
            if (Current.IsLocal)
            {
                return;
            }

            IExecutionProvider provider = _providerRegistry.GetRequiredProvider(
                BuiltInExecutionProviderIds.Local);
            IExecutionRuntime runtime = await provider.ConnectAsync(
                new ExecutionConnectionRequest(null, new Dictionary<string, string>()),
                cancellationToken);
            ExecutionContext next = new(
                Guid.NewGuid(),
                Interlocked.Increment(ref _version),
                provider.Id,
                null,
                runtime);
            await ReplaceAsync(next);
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        UnsubscribeFromConnectionLoss(Current.Runtime);
        await Current.Runtime.DisposeAsync();
        _switchLock.Dispose();
    }

    private async Task ReplaceAsync(ExecutionContext next)
    {
        ExecutionContext previous = Current;
        UnsubscribeFromConnectionLoss(previous.Runtime);
        Current = next;
        SubscribeToConnectionLoss(next.Runtime);
        CurrentChanged?.Invoke(this, new ExecutionContextChangedEventArgs(previous, next));
        if (!ReferenceEquals(previous.Runtime, next.Runtime))
        {
            await previous.Runtime.DisposeAsync();
        }
    }

    private void SubscribeToConnectionLoss(IExecutionRuntime runtime)
    {
        if (runtime is IConnectionAwareExecutionRuntime connectionAwareRuntime)
        {
            connectionAwareRuntime.ConnectionLost += Runtime_ConnectionLost;
        }
    }

    private void UnsubscribeFromConnectionLoss(IExecutionRuntime runtime)
    {
        if (runtime is IConnectionAwareExecutionRuntime connectionAwareRuntime)
        {
            connectionAwareRuntime.ConnectionLost -= Runtime_ConnectionLost;
        }
    }

    private void Runtime_ConnectionLost(object? sender, Exception exception)
    {
        ExecutionContext context = Current;
        if (ReferenceEquals(context.Runtime, sender))
        {
            ConnectionLost?.Invoke(this, new ExecutionConnectionLostEventArgs(context, exception));
        }
    }
}
