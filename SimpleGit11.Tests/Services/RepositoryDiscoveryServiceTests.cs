using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class RepositoryDiscoveryServiceTests
{
    [TestMethod]
    public void TryOpenRepository_LinkedWorktree_UsesWorktreeFolderName()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string mainRepositoryPath = Path.GetFullPath(
            temporaryDirectory.CreateDirectory("SimpleGit11"));
        string commonGitDirectory = Path.GetFullPath(
            temporaryDirectory.CreateDirectory("SimpleGit11/.git"));
        string linkedWorktreePath = Path.GetFullPath(
            temporaryDirectory.CreateDirectory("SimpleGit11-privatedocs"));
        string linkedGitDirectory = Path.GetFullPath(temporaryDirectory.CreateDirectory(
            "SimpleGit11/.git/worktrees/SimpleGit11-privatedocs"));

        temporaryDirectory.CreateFile("SimpleGit11/.git/HEAD", "ref: refs/heads/main");
        temporaryDirectory.CreateFile(
            "SimpleGit11-privatedocs/.git",
            $"gitdir: {linkedGitDirectory}");
        temporaryDirectory.CreateFile(
            "SimpleGit11/.git/worktrees/SimpleGit11-privatedocs/commondir",
            "../..");
        temporaryDirectory.CreateFile(
            "SimpleGit11/.git/worktrees/SimpleGit11-privatedocs/HEAD",
            "ref: refs/heads/privatedocs");

        RepositoryDiscoveryService service = new();

        RepositoryInfo? repository = service.TryOpenRepository(linkedWorktreePath);

        Assert.IsNotNull(repository);
        Assert.AreEqual("SimpleGit11-privatedocs", repository.Name);
        Assert.AreEqual(mainRepositoryPath, repository.MainWorktreePath);
        Assert.AreEqual(commonGitDirectory, repository.CommonGitDirectory);
        Assert.AreEqual(linkedWorktreePath, repository.Path);
        Assert.IsFalse(repository.IsMainWorktree);
    }

    [TestMethod]
    public async Task SearchAsync_LinkedWorktree_ReturnsMainRepositoryPath()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string mainRepositoryPath = Path.GetFullPath(
            temporaryDirectory.CreateDirectory("main/SimpleGit11"));
        string linkedWorktreePath = Path.GetFullPath(
            temporaryDirectory.CreateDirectory("search/SimpleGit11-privatedocs"));
        string linkedGitDirectory = Path.GetFullPath(temporaryDirectory.CreateDirectory(
            "main/SimpleGit11/.git/worktrees/SimpleGit11-privatedocs"));

        temporaryDirectory.CreateFile("main/SimpleGit11/.git/HEAD", "ref: refs/heads/main");
        temporaryDirectory.CreateFile(
            "search/SimpleGit11-privatedocs/.git",
            $"gitdir: {linkedGitDirectory}");
        temporaryDirectory.CreateFile(
            "main/SimpleGit11/.git/worktrees/SimpleGit11-privatedocs/commondir",
            "../..");
        temporaryDirectory.CreateFile(
            "main/SimpleGit11/.git/worktrees/SimpleGit11-privatedocs/HEAD",
            "ref: refs/heads/privatedocs");

        RepositorySearchService service = new(
            new RepositoryDiscoveryService(),
            new EmptyLocalSettingsStore());

        IReadOnlyList<RepositoryInfo> repositories = await service.SearchAsync(
            temporaryDirectory.GetPath("search"));

        Assert.HasCount(1, repositories);
        Assert.AreEqual("SimpleGit11", repositories[0].Name);
        Assert.AreEqual(mainRepositoryPath, repositories[0].Path);
        Assert.AreEqual(mainRepositoryPath, repositories[0].MainWorktreePath);
        Assert.IsTrue(repositories[0].IsMainWorktree);
        Assert.AreNotEqual(linkedWorktreePath, repositories[0].Path);
    }

    private sealed class EmptyLocalSettingsStore : ILocalSettingsStore
    {
        public string? GetString(string key)
        {
            return null;
        }

        public void SetString(string key, string value)
        {
        }
    }
}
