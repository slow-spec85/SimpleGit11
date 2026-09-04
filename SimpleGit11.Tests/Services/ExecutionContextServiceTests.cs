using SimpleGit11.Services.Execution;
using SimpleGit11.Services.Execution.Local;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class ExecutionContextServiceTests
{
    [TestMethod]
    public async Task ActivateAsync_SwitchesGitRunnerAndRaisesChangedEvent()
    {
        LocalExecutionRuntime localRuntime = CreateLocalRuntime();
        RecordingGitCommandRunner remoteGit = new("remote");
        FakeExecutionRuntime remoteRuntime = new("server.example", remoteGit);
        ExecutionProviderRegistry registry = new([
            new LocalExecutionProvider(localRuntime),
            new FakeExecutionProvider("test-remote", remoteRuntime)
        ]);
        await using ExecutionContextService service = new(registry, localRuntime);
        ContextualGitCommandRunner contextualRunner = new(service);
        ExecutionContextChangedEventArgs? changed = null;
        service.CurrentChanged += (_, args) => changed = args;

        await service.ActivateAsync(
            "test-remote",
            new ExecutionConnectionRequest("profile-1", new Dictionary<string, string>()));
        GitCommandResult result = await contextualRunner.RunAsync("/repo", ["status"]);

        Assert.AreEqual("remote", result.StandardOutput);
        Assert.AreEqual("server.example", service.Current.DisplayMachineName);
        Assert.IsFalse(service.Current.IsLocal);
        Assert.AreEqual("profile-1", service.Current.ConnectionProfileId);
        Assert.IsNotNull(changed);
        Assert.IsTrue(changed.Previous.IsLocal);
        Assert.AreSame(service.Current, changed.Current);
    }

    [TestMethod]
    public async Task UseLocalAsync_DisposesRemoteRuntimeAndRestoresLocalRunner()
    {
        LocalExecutionRuntime localRuntime = CreateLocalRuntime();
        FakeExecutionRuntime remoteRuntime = new(
            "server.example",
            new RecordingGitCommandRunner("remote"));
        ExecutionProviderRegistry registry = new([
            new LocalExecutionProvider(localRuntime),
            new FakeExecutionProvider("test-remote", remoteRuntime)
        ]);
        await using ExecutionContextService service = new(registry, localRuntime);

        await service.ActivateAsync(
            "test-remote",
            new ExecutionConnectionRequest(null, new Dictionary<string, string>()));
        await service.UseLocalAsync();

        Assert.IsTrue(service.Current.IsLocal);
        Assert.IsTrue(remoteRuntime.IsDisposed);
    }

    [TestMethod]
    public async Task ConnectionLost_CurrentRemoteRuntime_RaisesContextEventOnce()
    {
        LocalExecutionRuntime localRuntime = CreateLocalRuntime();
        FakeExecutionRuntime remoteRuntime = new(
            "server.example",
            new RecordingGitCommandRunner("remote"));
        ExecutionProviderRegistry registry = new([
            new LocalExecutionProvider(localRuntime),
            new FakeExecutionProvider("test-remote", remoteRuntime)
        ]);
        await using ExecutionContextService service = new(registry, localRuntime);
        List<ExecutionConnectionLostEventArgs> events = [];
        service.ConnectionLost += (_, args) => events.Add(args);
        await service.ActivateAsync(
            "test-remote",
            new ExecutionConnectionRequest("profile-1", new Dictionary<string, string>()));

        InvalidOperationException failure = new("connection closed");
        remoteRuntime.RaiseConnectionLost(failure);

        Assert.HasCount(1, events);
        Assert.AreSame(service.Current, events[0].Context);
        Assert.AreSame(failure, events[0].Exception);
    }

    private static LocalExecutionRuntime CreateLocalRuntime()
    {
        return new LocalExecutionRuntime(
            new GitCommandRunner(),
            new LocalRepositoryFileSystem(),
            new LocalRepositoryPathService(),
            new LocalRepositoryFileTransfer());
    }

    private sealed class FakeExecutionProvider : IExecutionProvider
    {
        private readonly IExecutionRuntime _runtime;

        public FakeExecutionProvider(string id, IExecutionRuntime runtime)
        {
            Id = id;
            _runtime = runtime;
        }

        public string Id { get; }

        public Task<IExecutionRuntime> ConnectAsync(
            ExecutionConnectionRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_runtime);
        }
    }

    private sealed class FakeExecutionRuntime : IExecutionRuntime, IConnectionAwareExecutionRuntime
    {
        public FakeExecutionRuntime(string displayMachineName, IGitCommandRunner git)
        {
            DisplayMachineName = displayMachineName;
            Git = git;
        }

        public string DisplayMachineName { get; }

        public event EventHandler<Exception>? ConnectionLost;

        public ExecutionCapabilities Capabilities => ExecutionCapabilities.Git;

        public IGitCommandRunner Git { get; }

        public IRepositoryFileSystem Files => throw new NotSupportedException();

        public IRepositoryPathService Paths => throw new NotSupportedException();

        public IRepositoryFileTransfer FileTransfer => throw new NotSupportedException();

        public bool IsDisposed { get; private set; }

        public void RaiseConnectionLost(Exception exception) =>
            ConnectionLost?.Invoke(this, exception);

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingGitCommandRunner : IGitCommandRunner
    {
        private readonly string _output;

        public RecordingGitCommandRunner(string output)
        {
            _output = output;
        }

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GitCommandResult(0, _output, string.Empty));
        }
    }
}
