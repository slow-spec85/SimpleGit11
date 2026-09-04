using System;
using System.Threading.Tasks;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services.Execution.Local;

public sealed class LocalExecutionRuntime : IExecutionRuntime
{
    public LocalExecutionRuntime(
        GitCommandRunner git,
        LocalRepositoryFileSystem files,
        LocalRepositoryPathService paths,
        LocalRepositoryFileTransfer fileTransfer)
    {
        Git = git;
        Files = files;
        Paths = paths;
        FileTransfer = fileTransfer;
    }

    public string DisplayMachineName => Environment.MachineName;

    public ExecutionCapabilities Capabilities =>
        ExecutionCapabilities.LocalMachine |
        ExecutionCapabilities.Git |
        ExecutionCapabilities.ReadFiles |
        ExecutionCapabilities.WriteFiles |
        ExecutionCapabilities.TransferFiles |
        ExecutionCapabilities.OpenInLocalFileExplorer;

    public IGitCommandRunner Git { get; }

    public IRepositoryFileSystem Files { get; }

    public IRepositoryPathService Paths { get; }

    public IRepositoryFileTransfer FileTransfer { get; }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
