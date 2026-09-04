using SimpleGit11.Services.Execution;
using SimpleGit11.Tests.TestInfrastructure;
using SimpleGit11.Services.Git.Execution;
using AppExecutionContext = SimpleGit11.Services.Execution.ExecutionContext;

namespace SimpleGit11.Tests.TestInfrastructure;

public sealed class TestExecutionContextService : IExecutionContextService
{
    public TestExecutionContextService(
        IRepositoryFileSystem files,
        RepositoryPathStyle pathStyle = RepositoryPathStyle.Posix,
        IRepositoryFileTransfer? fileTransfer = null,
        string? connectionProfileId = null,
        bool isLocal = false)
    {
        Current = new AppExecutionContext(
            Guid.NewGuid(),
            1,
            "test-remote",
            connectionProfileId,
            new Runtime(files, new TestRepositoryPathService(pathStyle), fileTransfer, isLocal));
    }

    public AppExecutionContext Current { get; }
    public event EventHandler<ExecutionContextChangedEventArgs>? CurrentChanged { add { } remove { } }
    public Task ActivateAsync(string providerId, ExecutionConnectionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task UseLocalAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    private sealed class Runtime : IExecutionRuntime
    {
        public Runtime(
            IRepositoryFileSystem files,
            IRepositoryPathService paths,
            IRepositoryFileTransfer? fileTransfer,
            bool isLocal)
        {
            Files = files;
            Paths = paths;
            FileTransfer = fileTransfer ?? new UnsupportedFileTransfer();
            Capabilities = ExecutionCapabilities.ReadFiles | ExecutionCapabilities.WriteFiles;
            if (isLocal)
            {
                Capabilities |= ExecutionCapabilities.LocalMachine;
            }
        }

        public string DisplayMachineName => "test-server";
        public ExecutionCapabilities Capabilities { get; }
        public IGitCommandRunner Git => throw new NotSupportedException();
        public IRepositoryFileSystem Files { get; }
        public IRepositoryPathService Paths { get; }
        public IRepositoryFileTransfer FileTransfer { get; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class UnsupportedFileTransfer : IRepositoryFileTransfer
    {
        public Task DownloadAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

public sealed class InMemoryRepositoryFileSystem : IRepositoryFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    public void Set(string path, byte[] content) => _files[path] = content;
    public byte[] Get(string path) => _files[path];
    public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(_files.ContainsKey(path));
    public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<IReadOnlyList<string>> EnumerateDirectoriesAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(_files[path]);
    public Task WriteAllBytesAtomicAsync(string path, byte[] content, CancellationToken cancellationToken = default)
    {
        _files[path] = content;
        return Task.CompletedTask;
    }
    public Task<RepositoryFileMetadata?> GetMetadataAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<RepositoryFileMetadata?>(null);
    public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
    {
        _files.Remove(path);
        return Task.CompletedTask;
    }
}
