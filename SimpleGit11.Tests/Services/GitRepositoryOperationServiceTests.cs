using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Services.Execution;
using SimpleGit11.Tests.TestInfrastructure;
using Stub = SimpleGit11.Tests.ViewModels.SettingsPullSettingsTests.ServiceStub;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitRepositoryOperationServiceTests
{
    private const string HostKey = "example.test ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIEjHsYQ5QhGVBp51EWFcIGu0pL7L+v4mXTXoD03QOZ8s";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CloneAsync_UnknownSshHost_WaitsForConfirmationAndStoresKeyBeforeGit(bool recursive)
    {
        using TemporaryDirectory directory = new();
        string knownHostsPath = directory.GetPath(".ssh/known_hosts");
        TaskCompletionSource<bool> confirmation = new();
        OpenSshService openSsh = CreateOpenSshService(knownHostsPath, _ => confirmation.Task);
        RecordingGitCommandRunner runner = new()
        {
            OnRun = () => StringAssert.Contains(File.ReadAllText(knownHostsPath), HostKey)
        };
        RepositoryInfo repository = new(directory.GetPath("project"), "project", "main");
        GitRepositoryOperationService service = new(
            new StubRepositoryDiscoveryService(repository), runner, openSshService: openSsh);

        Task<RepositoryInfo> clone = service.CloneAsync(directory.Path, "git@example.test:project.git", recursive);

        Assert.IsFalse(clone.IsCompleted);
        Assert.IsEmpty(runner.Arguments);
        confirmation.SetResult(true);
        Assert.AreSame(repository, await clone);
        CollectionAssert.AreEqual(
            recursive
                ? new[] { "clone", "--progress", "--recurse-submodules", "git@example.test:project.git" }
                : new[] { "clone", "--progress", "git@example.test:project.git" },
            runner.Arguments.ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CloneAsync_DeclinedSshHost_DoesNotStartGitOrStoreKey(bool recursive)
    {
        using TemporaryDirectory directory = new();
        string knownHostsPath = directory.GetPath(".ssh/known_hosts");
        OpenSshService openSsh = CreateOpenSshService(knownHostsPath, _ => Task.FromResult(false));
        RecordingGitCommandRunner runner = new();
        RepositoryInfo repository = new(directory.GetPath("project"), "project", "main");
        GitRepositoryOperationService service = new(
            new StubRepositoryDiscoveryService(repository), runner, openSshService: openSsh);

        GitRemoteOperationException exception = await Assert.ThrowsAsync<GitRemoteOperationException>(() =>
            service.CloneAsync(directory.Path, "git@example.test:project.git", recursive));

        Assert.AreEqual(GitRemoteOperationErrorKind.HostKeyVerification, exception.Kind);
        Assert.IsEmpty(runner.Arguments);
        Assert.IsFalse(File.Exists(knownHostsPath));
    }

    [TestMethod]
    public async Task CloneAsync_HostKeyScanFailure_DoesNotStartGit()
    {
        using TemporaryDirectory directory = new();
        OpenSshService openSsh = new(
            Stub.Create<IHostKeyConfirmationService>(),
            directory.GetPath("known_hosts"),
            (_, _, _) => Task.FromResult(new OpenSshService.ProcessResult(1, "", "Connection refused")));
        RecordingGitCommandRunner runner = new();
        RepositoryInfo repository = new(directory.GetPath("project"), "project", "main");
        GitRepositoryOperationService service = new(
            new StubRepositoryDiscoveryService(repository), runner, openSshService: openSsh);

        GitRemoteOperationException exception = await Assert.ThrowsAsync<GitRemoteOperationException>(() =>
            service.CloneAsync(directory.Path, "git@example.test:project.git"));

        StringAssert.Contains(exception.Message, "Connection refused");
        Assert.IsEmpty(runner.Arguments);
    }

    [TestMethod]
    public async Task CloneAsync_KnownSshHost_DoesNotPromptAgain()
    {
        using TemporaryDirectory directory = new();
        string knownHostsPath = directory.CreateFile(".ssh/known_hosts", HostKey + Environment.NewLine);
        OpenSshService openSsh = CreateOpenSshService(knownHostsPath,
            _ => throw new AssertFailedException("Known host must not require confirmation."));
        RecordingGitCommandRunner runner = new();
        RepositoryInfo repository = new(directory.GetPath("project"), "project", "main");
        GitRepositoryOperationService service = new(
            new StubRepositoryDiscoveryService(repository), runner, openSshService: openSsh);

        Assert.AreSame(repository, await service.CloneAsync(directory.Path, "git@example.test:project.git"));
        CollectionAssert.Contains(runner.Arguments.ToArray(), "clone");
        Assert.AreEqual(HostKey + Environment.NewLine, await File.ReadAllTextAsync(knownHostsPath));
    }

    [TestMethod]
    public async Task CloneAsync_CanceledHostConfirmation_DoesNotStartGitOrStoreKey()
    {
        using TemporaryDirectory directory = new();
        string knownHostsPath = directory.GetPath(".ssh/known_hosts");
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<bool> confirmation = new();
        OpenSshService openSsh = CreateOpenSshService(knownHostsPath,
            token => confirmation.Task.WaitAsync(token));
        RecordingGitCommandRunner runner = new();
        RepositoryInfo repository = new(directory.GetPath("project"), "project", "main");
        GitRepositoryOperationService service = new(
            new StubRepositoryDiscoveryService(repository), runner, openSshService: openSsh);

        Task<RepositoryInfo> clone = service.CloneAsync(
            directory.Path, "git@example.test:project.git", cancellationToken: cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => clone);
        Assert.IsEmpty(runner.Arguments);
        Assert.IsFalse(File.Exists(knownHostsPath));
    }

    [TestMethod]
    public async Task CloneAsync_Https_DoesNotScanOrConfirmSshHost()
    {
        using TemporaryDirectory directory = new();
        OpenSshService openSsh = new(
            Stub.Create<IHostKeyConfirmationService>(),
            directory.GetPath(".ssh/known_hosts"),
            (_, _, _) => throw new AssertFailedException("HTTPS clone must not scan SSH keys."));
        RecordingGitCommandRunner runner = new();
        RepositoryInfo repository = new(directory.GetPath("project"), "project", "main");
        GitRepositoryOperationService service = new(
            new StubRepositoryDiscoveryService(repository), runner, openSshService: openSsh);

        Assert.AreSame(repository, await service.CloneAsync(directory.Path, "https://example.test/project.git"));
        Assert.AreEqual("https://example.test/project.git", runner.Options?.HttpAuthentication?.Url);
    }

    [TestMethod]
    public async Task CloneAsync_WithRecursiveSubmodules_AddsRecurseSubmodulesOption()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RecordingGitCommandRunner runner = new();
        RepositoryInfo clonedRepository = new(
            temporaryDirectory.GetPath("project"),
            "project",
            "main");
        GitRepositoryOperationService service = new(
            new StubRepositoryDiscoveryService(clonedRepository),
            runner);
        using CancellationTokenSource cancellationTokenSource = new();

        RepositoryInfo result = await service.CloneAsync(
            temporaryDirectory.Path,
            "https://example.test/project.git",
            initializeSubmodulesRecursively: true,
            cancellationToken: cancellationTokenSource.Token);

        Assert.AreSame(clonedRepository, result);
        CollectionAssert.AreEqual(
            new[] { "clone", "--progress", "--recurse-submodules", "https://example.test/project.git" },
            new List<string>(runner.Arguments));
        Assert.AreEqual(cancellationTokenSource.Token, runner.CancellationToken);
        Assert.AreEqual(
            "https://example.test/project.git",
            runner.Options?.HttpAuthentication?.Url);
    }

    [TestMethod]
    public async Task CloneAsync_RemoteContext_UsesRemotePathAndDiscovery()
    {
        RecordingGitCommandRunner runner = new();
        DirectoryFileSystem files = new("/srv");
        TestExecutionContextService context = new(files);
        RepositoryInfo clonedRepository = new("/srv/project", "project", "main");
        RecordingExecutionDiscoveryService discovery = new(clonedRepository);
        using CancellationTokenSource cancellation = new();
        bool hostChecked = false;
        IOpenSshService openSsh = Stub.Create<IOpenSshService>((method, arguments) =>
        {
            Assert.AreEqual("EnsureTrustedAsync", method);
            Assert.AreEqual("git@example.test:project.git", arguments![0]);
            Assert.AreEqual(cancellation.Token, arguments[1]);
            hostChecked = true;
            return Task.CompletedTask;
        });
        runner.OnRun = () => Assert.IsTrue(hostChecked);
        GitRepositoryOperationService service = new(
            new StubRepositoryDiscoveryService(clonedRepository),
            runner,
            context,
            discovery,
            openSsh);

        RepositoryInfo result = await service.CloneAsync(
            "/srv",
            "git@example.test:project.git",
            cancellationToken: cancellation.Token);

        Assert.AreSame(clonedRepository, result);
        Assert.AreEqual("/srv", runner.WorkingDirectory);
        Assert.AreEqual("/srv/project", discovery.RequestedPath);
    }

    private static OpenSshService CreateOpenSshService(
        string knownHostsPath,
        Func<CancellationToken, Task<bool>> confirm)
    {
        return new OpenSshService(
            Stub.Create<IHostKeyConfirmationService>((_, arguments) => confirm((CancellationToken)arguments![1]!)),
            knownHostsPath,
            (_, _, _) => Task.FromResult(new OpenSshService.ProcessResult(0, HostKey, "")));
    }

    private sealed class StubRepositoryDiscoveryService(RepositoryInfo repository)
        : IGitRepositoryDiscoveryService
    {
        public RepositoryInfo? TryOpenRepository(string path) => repository;
    }

    private sealed class RecordingGitCommandRunner : IGitCommandRunner
    {
        public Action? OnRun { get; set; }
        public string WorkingDirectory { get; private set; } = "";
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public CancellationToken CancellationToken { get; private set; }
        public GitCommandOptions? Options { get; private set; }

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            OnRun?.Invoke();
            WorkingDirectory = workingDirectory;
            Arguments = arguments;
            CancellationToken = cancellationToken;
            Options = options;
            return Task.FromResult(new GitCommandResult(0, "", ""));
        }
    }

    private sealed class RecordingExecutionDiscoveryService(RepositoryInfo repository)
        : IExecutionRepositoryDiscoveryService
    {
        public string? RequestedPath { get; private set; }

        public Task<RepositoryInfo?> TryOpenRepositoryAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            RequestedPath = path;
            return Task.FromResult<RepositoryInfo?>(repository);
        }
    }

    private sealed class DirectoryFileSystem(string existingPath) : IRepositoryFileSystem
    {
        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
        public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(path == existingPath);
        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task WriteAllBytesAtomicAsync(string path, byte[] content, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<RepositoryFileMetadata?> GetMetadataAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<RepositoryFileMetadata?>(null);
    }
}
