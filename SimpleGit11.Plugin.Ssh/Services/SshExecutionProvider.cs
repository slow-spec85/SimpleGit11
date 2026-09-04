using System;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Plugin.Ssh.Services;

public sealed class SshExecutionProvider : IExecutionProvider
{
    public string Id => SshPlugin.ProviderId;

    public async Task<IExecutionRuntime> ConnectAsync(
        ExecutionConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        SshConnectionSettings settings = SshConnectionSettings.FromRequest(request);
        SshConnectionMonitor connectionMonitor = new();
        SshCommandSession commandSession = await SshCommandSession.ConnectAsync(
            settings,
            connectionMonitor,
            cancellationToken);
        try
        {
            RepositoryPathStyle pathStyle = await DetectPathStyleAsync(
                commandSession,
                cancellationToken);
            SshRepositoryFileSystem fileSystem = await SshRepositoryFileSystem.ConnectAsync(
                settings,
                connectionMonitor,
                cancellationToken);
            return new SshExecutionRuntime(
                settings.Host,
                pathStyle,
                commandSession,
                fileSystem,
                connectionMonitor);
        }
        catch
        {
            await commandSession.DisposeAsync();
            throw;
        }
    }

    private static async Task<RepositoryPathStyle> DetectPathStyleAsync(
        SshCommandSession session,
        CancellationToken cancellationToken)
    {
        SshCommandResult unix = await session.ExecuteAsync(
            "uname -s",
            cancellationToken: cancellationToken);
        if (unix.ExitCode == 0 && !string.IsNullOrWhiteSpace(unix.StandardOutput))
        {
            return RepositoryPathStyle.Posix;
        }

        SshCommandResult windows = await session.ExecuteAsync(
            "cmd.exe /d /s /c ver",
            cancellationToken: cancellationToken);
        if (windows.ExitCode == 0)
        {
            return RepositoryPathStyle.Windows;
        }

        throw new NotSupportedException(
            "The operating system of the SSH host could not be detected.");
    }
}
