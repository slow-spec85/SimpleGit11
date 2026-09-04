using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Services.Execution;

public interface IExecutionProvider
{
    string Id { get; }

    Task<IExecutionRuntime> ConnectAsync(
        ExecutionConnectionRequest request,
        CancellationToken cancellationToken = default);
}
