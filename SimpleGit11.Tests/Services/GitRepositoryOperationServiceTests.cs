using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;
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

    private sealed class StubRepositoryDiscoveryService(RepositoryInfo repository)
        : IGitRepositoryDiscoveryService
    {
        public RepositoryInfo? TryOpenRepository(string path) => repository;
    }

    private sealed class RecordingGitCommandRunner : IGitCommandRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Arguments = arguments;
            return Task.FromResult(new GitCommandResult(0, "", ""));
        }
    }
}
