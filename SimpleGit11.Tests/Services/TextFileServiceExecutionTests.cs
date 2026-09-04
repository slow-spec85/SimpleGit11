using System.Text;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Execution;
using SimpleGit11.Tests.TestInfrastructure;
using SimpleGit11.Services.Git.Execution;
using AppExecutionContext = SimpleGit11.Services.Execution.ExecutionContext;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class TextFileServiceExecutionTests
{
    [TestMethod]
    public async Task ReadAndWriteAsync_UsesActiveRemoteFileSystem()
    {
        MemoryFileSystem files = new();
        files.Set("/repo/file.txt", Encoding.UTF8.GetBytes("first\n"));
        MutableExecutionContextService context = new(files);
        TextFileService service = new(context);
        RepositoryInfo repository = new("/repo", "repo", "main");

        TextFileDocument document = await service.ReadAsync(repository, "file.txt");
        await service.WriteAsync(document, "second\n");

        Assert.AreEqual("first\n", document.Text);
        Assert.AreEqual("second\n", Encoding.UTF8.GetString(files.Get("/repo/file.txt")));
    }

    [TestMethod]
    public async Task WriteAsync_ContextChanged_ThrowsInsteadOfWritingToAnotherMachine()
    {
        MemoryFileSystem files = new();
        files.Set("/repo/file.txt", Encoding.UTF8.GetBytes("first"));
        MutableExecutionContextService context = new(files);
        TextFileService service = new(context);
        TextFileDocument document = await service.ReadAsync(
            new RepositoryInfo("/repo", "repo", "main"),
            "file.txt");
        context.Replace(files);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.WriteAsync(document, "changed"));
    }

    [TestMethod]
    public async Task ReadAsync_PathThroughRemoteSymbolicLink_IsRejected()
    {
        MemoryFileSystem files = new();
        files.Set("/repo/linked/secret.txt", Encoding.UTF8.GetBytes("secret"));
        files.AddSymbolicLink("/repo/linked");
        TextFileService service = new(new MutableExecutionContextService(files));

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.ReadAsync(
            new RepositoryInfo("/repo", "repo", "main"),
            "linked/secret.txt"));
    }

    private sealed class MutableExecutionContextService : IExecutionContextService
    {
        public MutableExecutionContextService(IRepositoryFileSystem files)
        {
            Current = Create(files);
        }

        public AppExecutionContext Current { get; private set; }
        public event EventHandler<ExecutionContextChangedEventArgs>? CurrentChanged;

        public void Replace(IRepositoryFileSystem files)
        {
            AppExecutionContext previous = Current;
            Current = Create(files);
            CurrentChanged?.Invoke(this, new ExecutionContextChangedEventArgs(previous, Current));
        }

        public Task ActivateAsync(string providerId, ExecutionConnectionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UseLocalAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private static AppExecutionContext Create(IRepositoryFileSystem files) => new(
            Guid.NewGuid(),
            1,
            "test-remote",
            null,
            new TestRuntime(files));
    }

    private sealed class TestRuntime : IExecutionRuntime
    {
        public TestRuntime(IRepositoryFileSystem files) => Files = files;
        public string DisplayMachineName => "server";
        public ExecutionCapabilities Capabilities => ExecutionCapabilities.ReadFiles | ExecutionCapabilities.WriteFiles;
        public IGitCommandRunner Git => throw new NotSupportedException();
        public IRepositoryFileSystem Files { get; }
        public IRepositoryPathService Paths { get; } = new TestRepositoryPathService(RepositoryPathStyle.Posix);
        public IRepositoryFileTransfer FileTransfer => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MemoryFileSystem : IRepositoryFileSystem
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
        private readonly HashSet<string> _symbolicLinks = new(StringComparer.Ordinal);
        public void Set(string path, byte[] content) => _files[path] = content;
        public void AddSymbolicLink(string path) => _symbolicLinks.Add(path);
        public byte[] Get(string path) => _files[path];
        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(_files.ContainsKey(path));
        public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> IsSymbolicLinkAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(_symbolicLinks.Contains(path));
        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(_files[path]);
        public Task WriteAllBytesAtomicAsync(string path, byte[] content, CancellationToken cancellationToken = default)
        {
            _files[path] = content;
            return Task.CompletedTask;
        }
        public Task<RepositoryFileMetadata?> GetMetadataAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<RepositoryFileMetadata?>(null);
    }
}
