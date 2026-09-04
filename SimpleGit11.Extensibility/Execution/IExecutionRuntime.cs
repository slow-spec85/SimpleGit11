using System;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services.Execution;

public interface IExecutionRuntime : IAsyncDisposable
{
    string DisplayMachineName { get; }

    ExecutionCapabilities Capabilities { get; }

    IGitCommandRunner Git { get; }

    IRepositoryFileSystem Files { get; }

    IRepositoryPathService Paths { get; }

    IRepositoryFileTransfer FileTransfer { get; }
}
