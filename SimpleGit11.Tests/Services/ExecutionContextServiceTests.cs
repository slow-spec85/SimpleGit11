using SimpleGit11.Services;
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

    [TestMethod]
    public async Task RunAsync_RemoteHttpsAuthenticationFailure_UsesLocalCredentialAndApprovesIt()
    {
        LocalExecutionRuntime localRuntime = CreateLocalRuntime();
        SequenceGitCommandRunner remoteGit = new(
            new GitCommandResult(128, "", "fatal: could not read Username"),
            new GitCommandResult(0, "success", ""));
        FakeExecutionRuntime remoteRuntime = new("server.example", remoteGit);
        ExecutionProviderRegistry registry = new([
            new LocalExecutionProvider(localRuntime),
            new FakeExecutionProvider("test-remote", remoteRuntime)
        ]);
        await using ExecutionContextService service = new(registry, localRuntime);
        RecordingCredentialService credentialService = new(
            new GitHttpAuthentication(
                "https://example.test/team/project.git",
                "user",
                "secret"));
        ContextualGitCommandRunner contextualRunner = new(service, credentialService);
        await service.ActivateAsync(
            "test-remote",
            new ExecutionConnectionRequest(null, new Dictionary<string, string>()));

        GitCommandResult result = await contextualRunner.RunAsync(
            "/repo",
            ["fetch", "origin"],
            new GitCommandOptions(
                HttpAuthentication: new GitHttpAuthentication(
                    "https://example.test/team/project.git")));

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(2, remoteGit.Options);
        Assert.IsNull(remoteGit.Options[0].HttpAuthentication?.Username);
        Assert.AreEqual("user", remoteGit.Options[1].HttpAuthentication?.Username);
        Assert.AreEqual(1, credentialService.GetCount);
        Assert.AreEqual(1, credentialService.ApproveCount);
        Assert.AreEqual(0, credentialService.RejectCount);
    }

    [TestMethod]
    public async Task RunAsync_RejectedHttpsCredential_ClearsItAndRetriesAuthenticationOnce()
    {
        LocalExecutionRuntime localRuntime = CreateLocalRuntime();
        SequenceGitCommandRunner remoteGit = new(
            new GitCommandResult(128, "", "fatal: Authentication failed"),
            new GitCommandResult(128, "", "fatal: Authentication failed"),
            new GitCommandResult(0, "success", ""));
        FakeExecutionRuntime remoteRuntime = new("server.example", remoteGit);
        ExecutionProviderRegistry registry = new([
            new LocalExecutionProvider(localRuntime),
            new FakeExecutionProvider("test-remote", remoteRuntime)
        ]);
        await using ExecutionContextService service = new(registry, localRuntime);
        RecordingCredentialService credentialService = new(
            new GitHttpAuthentication(
                "https://example.test/team/project.git",
                "user",
                "secret"));
        ContextualGitCommandRunner contextualRunner = new(service, credentialService);
        await service.ActivateAsync(
            "test-remote",
            new ExecutionConnectionRequest(null, new Dictionary<string, string>()));

        GitCommandResult result = await contextualRunner.RunAsync(
            "/repo",
            ["push", "origin"],
            new GitCommandOptions(
                HttpAuthentication: new GitHttpAuthentication(
                    "https://example.test/team/project.git")));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(2, credentialService.GetCount);
        Assert.AreEqual(1, credentialService.RejectCount);
        Assert.AreEqual(1, credentialService.ApproveCount);
    }

    [TestMethod]
    public async Task RunAsync_CredentialManagerFailure_PreservesLocalAndRemoteErrors()
    {
        LocalExecutionRuntime localRuntime = CreateLocalRuntime();
        SequenceGitCommandRunner remoteGit = new(
            new GitCommandResult(128, "", "fatal: could not read Username"));
        FakeExecutionRuntime remoteRuntime = new("server.example", remoteGit);
        ExecutionProviderRegistry registry = new([
            new LocalExecutionProvider(localRuntime),
            new FakeExecutionProvider("test-remote", remoteRuntime)
        ]);
        await using ExecutionContextService service = new(registry, localRuntime);
        ContextualGitCommandRunner contextualRunner = new(
            service,
            new FailingCredentialService("Git Credential Manager failed. TLS certificate failure"));
        await service.ActivateAsync(
            "test-remote",
            new ExecutionConnectionRequest(null, new Dictionary<string, string>()));

        GitCommandResult result = await contextualRunner.RunAsync(
            "/repo",
            ["fetch", "origin"],
            new GitCommandOptions(
                ThrowOnError: false,
                HttpAuthentication: new GitHttpAuthentication(
                    "https://example.test/team/project.git")));

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.StandardError, "Git Credential Manager failed. TLS certificate failure");
        StringAssert.Contains(result.StandardError, "fatal: could not read Username");
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

    private sealed class SequenceGitCommandRunner(params GitCommandResult[] results)
        : IGitCommandRunner
    {
        private int _index;

        public List<GitCommandOptions> Options { get; } = [];

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Options.Add(options ?? new GitCommandOptions());
            return Task.FromResult(results[_index++]);
        }
    }

    private sealed class RecordingCredentialService(GitHttpAuthentication credential)
        : ILocalGitCredentialService
    {
        public int GetCount { get; private set; }
        public int ApproveCount { get; private set; }
        public int RejectCount { get; private set; }

        public Task<GitHttpAuthentication?> GetAsync(
            string remoteUrl,
            CancellationToken cancellationToken = default)
        {
            GetCount++;
            return Task.FromResult<GitHttpAuthentication?>(credential);
        }

        public Task ApproveAsync(
            GitHttpAuthentication approvedCredential,
            CancellationToken cancellationToken = default)
        {
            ApproveCount++;
            return Task.CompletedTask;
        }

        public Task RejectAsync(
            GitHttpAuthentication rejectedCredential,
            CancellationToken cancellationToken = default)
        {
            RejectCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingCredentialService(string message) : ILocalGitCredentialService
    {
        public Task<GitHttpAuthentication?> GetAsync(
            string remoteUrl,
            CancellationToken cancellationToken = default)
        {
            throw new GitCommandException(message, 1);
        }

        public Task ApproveAsync(
            GitHttpAuthentication credential,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RejectAsync(
            GitHttpAuthentication credential,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
