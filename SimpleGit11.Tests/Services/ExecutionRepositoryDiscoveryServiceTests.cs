using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Execution;
using SimpleGit11.Services.Execution.Local;
using SimpleGit11.Tests.TestInfrastructure;
using SimpleGit11.Services.Git.Execution;
using AppExecutionContext = SimpleGit11.Services.Execution.ExecutionContext;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class ExecutionRepositoryDiscoveryServiceTests
{
    [TestMethod]
    [DataRow("D:/repos/main", "D:/repos/main/.git", "D:\\repos\\main\\.git", true)]
    [DataRow("D:/repos/feature", "D:/repos/main/.git/worktrees/feature", "D:/repos/main/.git", false)]
    public async Task TryOpenRepositoryAsync_WindowsGitPaths_NormalizesRepositoryIdentity(
        string root, string gitDirectory, string commonDirectory, bool isMainWorktree)
    {
        ScriptedGitCommandRunner runner = new(new Dictionary<string, GitCommandResult>
        {
            ["rev-parse --show-toplevel"] = Success(root + "\n"),
            ["rev-parse --path-format=absolute --git-dir"] = Success(gitDirectory + "\n"),
            ["rev-parse --path-format=absolute --git-common-dir"] = Success(commonDirectory + "\n"),
            ["symbolic-ref --quiet --short HEAD"] = Success("main\n")
        });
        ExecutionRepositoryDiscoveryService service = new(
            runner, new TestExecutionContextService(new LocalRepositoryPathService()));

        RepositoryInfo? repository = await service.TryOpenRepositoryAsync(root);

        Assert.IsNotNull(repository);
        Assert.AreEqual(root.Replace('/', '\\'), repository.Path);
        Assert.AreEqual(@"D:\repos\main\.git", repository.CommonGitDirectory);
        Assert.AreEqual(@"D:\repos\main", repository.MainWorktreePath);
        Assert.AreEqual(isMainWorktree, repository.IsMainWorktree);
    }

    [TestMethod]
    public async Task TryOpenRepositoryAsync_PosixRepository_ReturnsRemoteIdentity()
    {
        ScriptedGitCommandRunner runner = new(new Dictionary<string, GitCommandResult>
        {
            ["rev-parse --show-toplevel"] = Success("/srv/repo\n"),
            ["rev-parse --path-format=absolute --git-dir"] = Success("/srv/repo/.git\n"),
            ["rev-parse --path-format=absolute --git-common-dir"] = Success("/srv/repo/.git\n"),
            ["symbolic-ref --quiet --short HEAD"] = Success("main\n")
        });
        TestExecutionContextService context = new(new TestRepositoryPathService(RepositoryPathStyle.Posix));
        ExecutionRepositoryDiscoveryService service = new(runner, context);

        RepositoryInfo? repository = await service.TryOpenRepositoryAsync("/srv/repo/subdirectory");

        Assert.IsNotNull(repository);
        Assert.AreEqual("/srv/repo", repository.Path);
        Assert.AreEqual("repo", repository.Name);
        Assert.AreEqual("main", repository.CurrentBranch);
        Assert.AreEqual("/srv/repo/.git", repository.CommonGitDirectory);
        Assert.IsTrue(repository.IsMainWorktree);
    }

    [TestMethod]
    public async Task TryOpenRepositoryAsync_NotARepository_ReturnsNull()
    {
        ScriptedGitCommandRunner runner = new(new Dictionary<string, GitCommandResult>
        {
            ["rev-parse --show-toplevel"] = new GitCommandResult(128, "", "not a repository")
        });
        TestExecutionContextService context = new(new TestRepositoryPathService(RepositoryPathStyle.Posix));
        ExecutionRepositoryDiscoveryService service = new(runner, context);

        RepositoryInfo? repository = await service.TryOpenRepositoryAsync("/srv/not-a-repo");

        Assert.IsNull(repository);
    }

    private static GitCommandResult Success(string output) => new(0, output, "");

    private sealed class ScriptedGitCommandRunner : IGitCommandRunner
    {
        private readonly IReadOnlyDictionary<string, GitCommandResult> _results;

        public ScriptedGitCommandRunner(IReadOnlyDictionary<string, GitCommandResult> results)
        {
            _results = results;
        }

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_results[string.Join(' ', arguments)]);
        }
    }

    private sealed class TestExecutionContextService : IExecutionContextService
    {
        public TestExecutionContextService(IRepositoryPathService paths)
        {
            Current = new AppExecutionContext(
                Guid.NewGuid(),
                1,
                "test-remote",
                null,
                new TestRuntime(paths));
        }

        public AppExecutionContext Current { get; }

        public event EventHandler<ExecutionContextChangedEventArgs>? CurrentChanged
        {
            add { }
            remove { }
        }

        public Task ActivateAsync(string providerId, ExecutionConnectionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UseLocalAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestRuntime : IExecutionRuntime
    {
        public TestRuntime(IRepositoryPathService paths)
        {
            Paths = paths;
        }

        public string DisplayMachineName => "server";
        public ExecutionCapabilities Capabilities => ExecutionCapabilities.Git;
        public IGitCommandRunner Git => throw new NotSupportedException();
        public IRepositoryFileSystem Files => throw new NotSupportedException();
        public IRepositoryPathService Paths { get; }
        public IRepositoryFileTransfer FileTransfer => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
