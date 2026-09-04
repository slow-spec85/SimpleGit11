using System.Text;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitIgnoreExecutionTests
{
    [TestMethod]
    public async Task AddAsync_RemoteContext_UpdatesGitIgnoreThroughRepositoryFileSystem()
    {
        InMemoryRepositoryFileSystem files = new();
        files.Set("/repo/.gitignore", Encoding.UTF8.GetBytes("/bin/\n"));
        GitIgnoreService service = new(new TestExecutionContextService(files));
        RepositoryInfo repository = new("/repo", "repo", "main");
        GitChangedFile changedFile = new("logs/*.txt", "Untracked");

        await service.AddAsync(repository, changedFile);

        string content = Encoding.UTF8.GetString(files.Get("/repo/.gitignore"));
        Assert.AreEqual($"/bin/\n/logs/\\*.txt{Environment.NewLine}", content);
    }
}
