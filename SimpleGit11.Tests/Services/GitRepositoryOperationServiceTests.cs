using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Services.Execution;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitRepositoryOperationServiceTests
{
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

        RepositoryInfo result = await service.CloneAsync(
            temporaryDirectory.Path,
            "https://example.test/project.git",
            initializeSubmodulesRecursively: true);

        Assert.AreSame(clonedRepository, result);
        CollectionAssert.AreEqual(
            new[] { "clone", "--progress", "--recurse-submodules", "https://example.test/project.git" },
            new List<string>(runner.Arguments));
    }

    [TestMethod]
    public async Task CloneAsync_RemoteContext_UsesRemotePathAndDiscovery()
    {
        RecordingGitCommandRunner runner = new();
        DirectoryFileSystem files = new("/srv");
        TestExecutionContextService context = new(files);
        RepositoryInfo clonedRepository = new("/srv/project", "project", "main");
        RecordingExecutionDiscoveryService discovery = new(clonedRepository);
        GitRepositoryOperationService service = new(
            new StubRepositoryDiscoveryService(clonedRepository),
            runner,
            context,
            discovery);

        RepositoryInfo result = await service.CloneAsync(
            "/srv",
            "https://example.test/project.git");

        Assert.AreSame(clonedRepository, result);
        Assert.AreEqual("/srv", runner.WorkingDirectory);
        Assert.AreEqual("/srv/project", discovery.RequestedPath);
    }

    private sealed class StubRepositoryDiscoveryService(RepositoryInfo repository)
        : IGitRepositoryDiscoveryService
    {
        public RepositoryInfo? TryOpenRepository(string path) => repository;
    }

    private sealed class RecordingGitCommandRunner : IGitCommandRunner
    {
        public string WorkingDirectory { get; private set; } = "";
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            WorkingDirectory = workingDirectory;
            Arguments = arguments;
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
