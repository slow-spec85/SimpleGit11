using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Execution;
using SimpleGit11.Tests.TestInfrastructure;
using AppExecutionContext = SimpleGit11.Services.Execution.ExecutionContext;

namespace SimpleGit11.Tests.Presentation;

internal sealed class ConnectionTestContexts : IExecutionContextService
{
    public AppExecutionContext Current { get; private set; } = Create(true);
    public List<ExecutionConnectionRequest> Requests { get; } = [];
    public Func<ExecutionConnectionRequest, Task>? Connect { get; set; }
    public int UseLocalCalls { get; private set; }
    public event EventHandler<ExecutionContextChangedEventArgs>? CurrentChanged;
    public event EventHandler<ExecutionConnectionLostEventArgs>? ConnectionLost;

    public async Task ActivateAsync(string providerId, ExecutionConnectionRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (Connect is not null)
        {
            await Connect(request);
        }
        Switch(false, providerId);
    }

    public Task UseLocalAsync(CancellationToken cancellationToken = default)
    {
        UseLocalCalls++;
        Switch(true);
        return Task.CompletedTask;
    }

    public void Switch(bool local, string providerId = "test-remote")
    {
        AppExecutionContext previous = Current;
        Current = Create(local) with { ProviderId = local ? "local" : providerId };
        CurrentChanged?.Invoke(this, new(previous, Current));
    }

    public void Lose(AppExecutionContext context) =>
        ConnectionLost?.Invoke(this, new(context, new IOException("Connection lost")));

    private static AppExecutionContext Create(bool local) =>
        new TestExecutionContextService(new InMemoryRepositoryFileSystem(), isLocal: local).Current;
}

internal sealed class ConnectionTestLocalization : ILocalizationService
{
    public AppLanguage CurrentLanguage => AppLanguage.English;
    public string GetString(string resourceKey) => resourceKey + " {0}";
    public void ApplyLanguage() { }
    public void SetLanguage(AppLanguage language) { }
}
