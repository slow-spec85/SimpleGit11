using System;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Services.Execution;

public interface IExecutionContextService
{
    ExecutionContext Current { get; }

    event EventHandler<ExecutionContextChangedEventArgs>? CurrentChanged;

    event EventHandler<ExecutionConnectionLostEventArgs>? ConnectionLost
    {
        add { }
        remove { }
    }

    Task ActivateAsync(
        string providerId,
        ExecutionConnectionRequest request,
        CancellationToken cancellationToken = default);

    Task UseLocalAsync(CancellationToken cancellationToken = default);
}
