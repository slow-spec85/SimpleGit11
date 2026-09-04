using System;
using System.Threading.Tasks;
using SimpleGit11.Services.Execution;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Plugin.Ssh.Services;

public sealed class SshExecutionRuntime : IExecutionRuntime, IConnectionAwareExecutionRuntime
{
    private readonly SshCommandSession _commandSession;
    private readonly SshRepositoryFileSystem _fileSystem;

    public SshExecutionRuntime(
        string displayMachineName,
        RepositoryPathStyle pathStyle,
        SshCommandSession commandSession,
        SshRepositoryFileSystem fileSystem,
        SshConnectionMonitor connectionMonitor)
    {
        DisplayMachineName = displayMachineName;
        _commandSession = commandSession;
        _fileSystem = fileSystem;
        Git = new SshGitCommandRunner(commandSession, pathStyle);
        Files = fileSystem;
        Paths = new RemoteRepositoryPathService(pathStyle);
        FileTransfer = fileSystem;
        connectionMonitor.ConnectionLost += (_, exception) =>
            ConnectionLost?.Invoke(this, exception);
    }

    public event EventHandler<Exception>? ConnectionLost;

    public string DisplayMachineName { get; }

    public ExecutionCapabilities Capabilities =>
        ExecutionCapabilities.Git |
        ExecutionCapabilities.ReadFiles |
        ExecutionCapabilities.WriteFiles |
        ExecutionCapabilities.TransferFiles;

    public IGitCommandRunner Git { get; }

    public IRepositoryFileSystem Files { get; }

    public IRepositoryPathService Paths { get; }

    public IRepositoryFileTransfer FileTransfer { get; }

    public async ValueTask DisposeAsync()
    {
        Exception? firstException = null;
        try
        {
            await _fileSystem.DisposeAsync();
        }
        catch (Exception exception)
        {
            firstException = exception;
        }

        try
        {
            await _commandSession.DisposeAsync();
        }
        catch when (firstException is not null)
        {
        }

        if (firstException is not null)
        {
            throw firstException;
        }
    }
}
