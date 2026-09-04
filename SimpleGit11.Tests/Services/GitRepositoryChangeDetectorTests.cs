using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitRepositoryChangeDetectorTests
{
    [TestMethod]
    public async Task HasChangedAsync_FirstSnapshot_ReturnsTrue()
    {
        FakeGitCommandRunner runner = new();
        GitRepositoryChangeDetector detector = CreateDetector(runner);

        bool hasChanged = await detector.HasChangedAsync(CreateRepository("repository-a"));

        Assert.IsTrue(hasChanged);
    }

    [TestMethod]
    public async Task HasChangedAsync_UnchangedSnapshot_ReturnsFalse()
    {
        FakeGitCommandRunner runner = new();
        GitRepositoryChangeDetector detector = CreateDetector(runner);
        RepositoryInfo repository = CreateRepository("repository-a");

        _ = await detector.HasChangedAsync(repository);
        bool hasChanged = await detector.HasChangedAsync(repository);

        Assert.IsFalse(hasChanged);
    }

    [TestMethod]
    public async Task HasChangedAsync_StatusChanged_ReturnsTrue()
    {
        FakeGitCommandRunner runner = new();
        GitRepositoryChangeDetector detector = CreateDetector(runner);
        RepositoryInfo repository = CreateRepository("repository-a");
        _ = await detector.HasChangedAsync(repository);
        runner.StatusOutput = "1 M. N... 100644 100644 100644 abc def file.txt\0";

        bool hasChanged = await detector.HasChangedAsync(repository);

        Assert.IsTrue(hasChanged);
    }

    [TestMethod]
    public async Task HasChangedAsync_ReferencesChanged_ReturnsTrue()
    {
        FakeGitCommandRunner runner = new();
        GitRepositoryChangeDetector detector = CreateDetector(runner);
        RepositoryInfo repository = CreateRepository("repository-a");
        _ = await detector.HasChangedAsync(repository);
        runner.ReferencesOutput = "refs/heads/main\0new-object-id\n";

        bool hasChanged = await detector.HasChangedAsync(repository);

        Assert.IsTrue(hasChanged);
    }

    [TestMethod]
    public async Task HasChangedAsync_DifferentRepositories_UseIndependentSnapshots()
    {
        FakeGitCommandRunner runner = new();
        GitRepositoryChangeDetector detector = CreateDetector(runner);

        _ = await detector.HasChangedAsync(CreateRepository("repository-a"));
        bool hasChanged = await detector.HasChangedAsync(CreateRepository("repository-b"));

        Assert.IsTrue(hasChanged);
    }

    [TestMethod]
    public async Task HasChangedAsync_PosixPathsWithDifferentCase_UseIndependentSnapshots()
    {
        FakeGitCommandRunner runner = new();
        TestExecutionContextService context = new(new InMemoryRepositoryFileSystem());
        GitRepositoryChangeDetector detector = new(
            runner,
            new GitStatusService(runner),
            context);

        _ = await detector.HasChangedAsync(new RepositoryInfo("/srv/Repo", "Repo", "main"));
        bool hasChanged = await detector.HasChangedAsync(
            new RepositoryInfo("/srv/repo", "repo", "main"));

        Assert.IsTrue(hasChanged);
    }

    [TestMethod]
    public async Task EnsureBaselineAsync_FirstComparisonDoesNotReportChange()
    {
        FakeGitCommandRunner runner = new();
        GitRepositoryChangeDetector detector = CreateDetector(runner);
        RepositoryInfo repository = CreateRepository("repository-a");

        await detector.EnsureBaselineAsync(repository);
        bool hasChanged = await detector.HasChangedAsync(repository);

        Assert.IsFalse(hasChanged);
    }

    [TestMethod]
    public async Task HasChangedAsync_UsesLightweightStatusAndReferenceCommands()
    {
        FakeGitCommandRunner runner = new();
        GitRepositoryChangeDetector detector = CreateDetector(runner);

        _ = await detector.HasChangedAsync(CreateRepository("repository-a"));

        Assert.IsTrue(runner.Commands.Any(arguments => arguments.SequenceEqual(
        [
            "status",
            "--porcelain=v2",
            "-z",
            "--branch",
            "--no-ahead-behind",
            "--untracked-files=normal",
            "--no-renames"
        ])));
        Assert.IsTrue(runner.Commands.Any(arguments => arguments.SequenceEqual(
        [
            "for-each-ref",
            "--sort=refname",
            "--format=%(refname)%00%(objectname)"
        ])));
    }

    [TestMethod]
    public async Task HasChangedAsync_WorkingTreeChanged_DetectsRealRepositoryChange()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        GitCommandRunner runner = new();
        GitRepositoryChangeDetector detector = CreateDetector(runner);

        _ = await detector.HasChangedAsync(repository.Repository);
        Assert.IsFalse(await detector.HasChangedAsync(repository.Repository));
        _ = repository.WriteFile("new-file.txt", "content");

        bool hasChanged = await detector.HasChangedAsync(repository.Repository);

        Assert.IsTrue(hasChanged);
    }

    [TestMethod]
    public async Task HasChangedAsync_MergeStateChanged_DetectsRealRepositoryChange()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        await repository.RunGitAsync("commit", "--allow-empty", "-m", "initial");
        GitCommandRunner runner = new();
        GitRepositoryChangeDetector detector = CreateDetector(runner);

        _ = await detector.HasChangedAsync(repository.Repository);
        Assert.IsFalse(await detector.HasChangedAsync(repository.Repository));
        repository.WriteFile(".git/MERGE_HEAD", new string('a', 40));
        repository.WriteFile(".git/MERGE_MSG", "Merge branch 'feature'");

        bool hasChanged = await detector.HasChangedAsync(repository.Repository);

        Assert.IsTrue(hasChanged);
    }

    private static RepositoryInfo CreateRepository(string name)
    {
        return new RepositoryInfo(Path.Combine(Environment.CurrentDirectory, name), name, "main");
    }

    private sealed class FakeGitCommandRunner : IGitCommandRunner
    {
        private readonly object _syncRoot = new();

        public string StatusOutput { get; set; } = "# branch.oid object-id\n# branch.head main\n";

        public string ReferencesOutput { get; set; } = "refs/heads/main\0object-id\n";

        public List<IReadOnlyList<string>> Commands { get; } = [];

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            lock (_syncRoot)
            {
                Commands.Add(arguments.ToArray());
            }

            string output = arguments[0] switch
            {
                "status" => StatusOutput,
                "rev-parse" => ".git\n",
                _ => ReferencesOutput
            };
            return Task.FromResult(new GitCommandResult(0, output, ""));
        }
    }

    private static GitRepositoryChangeDetector CreateDetector(IGitCommandRunner runner)
    {
        return new GitRepositoryChangeDetector(runner, new GitStatusService(runner));
    }
}
