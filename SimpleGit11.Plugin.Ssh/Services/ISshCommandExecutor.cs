using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Plugin.Ssh.Services;

internal interface ISshCommandExecutor : IAsyncDisposable
{
    Task<SshCommandResult> ExecuteAsync(
        string commandText,
        string? standardInput = null,
        CancellationToken cancellationToken = default);
}
