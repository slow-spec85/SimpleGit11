using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Services.Execution.Local;

public sealed class LocalExecutionProvider : IExecutionProvider
{
    private readonly LocalExecutionRuntime _runtime;

    public LocalExecutionProvider(LocalExecutionRuntime runtime)
    {
        _runtime = runtime;
    }

    public string Id => BuiltInExecutionProviderIds.Local;

    public Task<IExecutionRuntime> ConnectAsync(
        ExecutionConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IExecutionRuntime>(_runtime);
    }
}
