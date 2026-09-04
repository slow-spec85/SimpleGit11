using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Execution;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitArchiveExecutionTests
{
    [TestMethod]
    public async Task CreateAsync_RemoteContext_DownloadsArchiveAndDeletesRemoteTemporaryFile()
    {
        using TemporaryDirectory output = new();
        InMemoryRepositoryFileSystem files = new();
        MemoryFileTransfer transfer = new(files);
        ArchiveGitRunner runner = new(files);
        GitArchiveService service = new(
            runner,
            new TestExecutionContextService(files, fileTransfer: transfer));
        string outputPath = Path.Combine(output.Path, "repository.zip");
        RepositoryInfo repository = new(
            "/srv/repo",
            "repo",
            "main",
            "/srv/repo/.git");

        await service.CreateAsync(
            repository,
            new GitArchiveRequest("HEAD", outputPath, GitArchiveFormat.Zip, "repo"),
            CancellationToken.None);

        CollectionAssert.AreEqual("archive"u8.ToArray(), await File.ReadAllBytesAsync(outputPath));
        Assert.IsFalse(await files.FileExistsAsync(runner.RemoteOutputPath));
    }

    private sealed class ArchiveGitRunner : IGitCommandRunner
    {
        private readonly InMemoryRepositoryFileSystem _files;
        public ArchiveGitRunner(InMemoryRepositoryFileSystem files) => _files = files;
        public string RemoteOutputPath { get; private set; } = "";

        public Task<GitCommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, GitCommandOptions? options = null, CancellationToken cancellationToken = default)
        {
            RemoteOutputPath = arguments.Single(argument => argument.StartsWith("--output=", StringComparison.Ordinal))[9..];
            _files.Set(RemoteOutputPath, "archive"u8.ToArray());
            return Task.FromResult(new GitCommandResult(0, "", ""));
        }
    }

    private sealed class MemoryFileTransfer : IRepositoryFileTransfer
    {
        private readonly InMemoryRepositoryFileSystem _files;
        public MemoryFileTransfer(InMemoryRepositoryFileSystem files) => _files = files;
        public Task DownloadAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default) =>
            File.WriteAllBytesAsync(destinationPath, _files.Get(sourcePath), cancellationToken);
    }
}
