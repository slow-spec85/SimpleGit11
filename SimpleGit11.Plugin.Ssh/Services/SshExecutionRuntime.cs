using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Services.Execution;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Plugin.Ssh.Services;

public sealed class SshExecutionRuntime :
    IExecutionRuntime,
    IConnectionAwareExecutionRuntime,
    IRemoteSshHostKeyStore,
    IRemoteSshIdentityStore
{
    private readonly SshCommandSession _commandSession;
    private readonly SshRepositoryFileSystem _fileSystem;
    private readonly RemoteSshHostKeyStore _hostKeyStore;
    private readonly RemoteSshIdentityStore _identityStore;

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
        _hostKeyStore = new RemoteSshHostKeyStore(commandSession, fileSystem, Paths);
        _identityStore = new RemoteSshIdentityStore(commandSession, fileSystem, Paths);
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

    public Task<string> ReadKnownHostsAsync(CancellationToken cancellationToken = default)
    {
        return _hostKeyStore.ReadKnownHostsAsync(cancellationToken);
    }

    public Task<IReadOnlyList<string>> ScanHostKeysAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        return _hostKeyStore.ScanHostKeysAsync(host, port, cancellationToken);
    }

    public Task AppendKnownHostsAsync(
        IReadOnlyList<string> keyLines,
        CancellationToken cancellationToken = default)
    {
        return _hostKeyStore.AppendKnownHostsAsync(keyLines, cancellationToken);
    }

    public Task<IReadOnlyList<SshIdentity>> GetIdentitiesAsync(
        CancellationToken cancellationToken = default) =>
        _identityStore.GetIdentitiesAsync(cancellationToken);

    public Task<SshIdentity> CreateIdentityAsync(
        string host,
        int port,
        string? user,
        string machineName,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        return _identityStore.CreateIdentityAsync(host, port, user, machineName, passphrase, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetIdentityConfigurationReferencesAsync(
        string privateKeyPath,
        CancellationToken cancellationToken = default) =>
        _identityStore.GetIdentityConfigurationReferencesAsync(privateKeyPath, cancellationToken);

    public Task DeleteIdentityAsync(
        string privateKeyPath,
        bool removeExternalReferences,
        CancellationToken cancellationToken = default) =>
        _identityStore.DeleteIdentityAsync(privateKeyPath, removeExternalReferences, cancellationToken);

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
