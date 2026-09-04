using System.Text.Json;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Execution;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class RecentRepositoriesServiceTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Add_LocalPathWithDifferentSeparators_ReplacesExistingRepository(bool hasCommonDirectory)
    {
        MemorySettingsStore settings = new();
        RecentRepositoriesService service = CreateLocalService(settings);
        service.Add(new RepositoryInfo(@"D:\repos\main", "main", "old",
            hasCommonDirectory ? @"D:\repos\main\.git" : ""));

        IReadOnlyList<RepositoryInfo> result = service.Add(new RepositoryInfo(
            "D:/repos/main", "main", "updated",
            hasCommonDirectory ? "D:/repos/main/.git" : "", "D:/repos/main"));

        Assert.HasCount(1, result);
        Assert.AreEqual(@"D:\repos\main", result[0].Path);
        Assert.AreEqual(@"D:\repos\main", result[0].MainWorktreePath);
        Assert.AreEqual("updated", result[0].CurrentBranch);
        Assert.AreEqual(hasCommonDirectory ? @"D:\repos\main\.git" : "", result[0].CommonGitDirectory);
        Assert.HasCount(1, service.Load());
        Assert.HasCount(1, JsonSerializer.Deserialize<List<RepositoryInfo>>(settings.GetString("RecentRepositories")!)!);
    }

    [TestMethod]
    public void Load_LocalStoredDuplicates_NormalizesPathsAndKeepsMostRecentEntry()
    {
        MemorySettingsStore settings = new();
        settings.SetString("RecentRepositories", JsonSerializer.Serialize(new[]
        {
            new RepositoryInfo("D:/repos/feature", "feature", "new", "D:/repos/main/.git", "D:/repos/main", false),
            new RepositoryInfo(@"d:\repos\main", "main", "old", @"d:\repos\main\.git\")
        }));

        IReadOnlyList<RepositoryInfo> result = CreateLocalService(settings).Load();

        Assert.HasCount(1, result);
        Assert.AreEqual(@"D:\repos\feature", result[0].Path);
        Assert.AreEqual(@"D:\repos\main\.git", result[0].CommonGitDirectory);
        Assert.AreEqual(@"D:\repos\main", result[0].MainWorktreePath);
        Assert.AreEqual("new", result[0].CurrentBranch);
        Assert.IsFalse(result[0].IsMainWorktree);
    }

    [TestMethod]
    public void Remove_LocalPathWithDifferentSeparators_RemovesRepository()
    {
        RecentRepositoriesService service = CreateLocalService(new MemorySettingsStore());
        service.Add(new RepositoryInfo(@"D:\repos\main", "main", "main", @"D:\repos\main\.git"));

        Assert.IsEmpty(service.Remove(new RepositoryInfo(
            "d:/repos/main", "main", "main", "d:/repos/main/.git/")));
        Assert.IsEmpty(service.Load());
    }

    [TestMethod]
    public void Add_PosixPaths_PreservesCaseAndLiteralBackslashes()
    {
        RecentRepositoriesService service = CreateRemoteService(new MemorySettingsStore(), "server-one");
        service.Add(new RepositoryInfo("/srv/Repo", "Repo", "main"));
        service.Add(new RepositoryInfo("/srv/repo", "repo", "main"));
        service.Add(new RepositoryInfo(@"/srv/repo\name", "name", "main"));

        IReadOnlyList<RepositoryInfo> result = service.Load();

        Assert.HasCount(3, result);
        Assert.AreEqual(@"/srv/repo\name", result[0].Path);
        Assert.AreEqual("/srv/repo", result[1].Path);
        Assert.AreEqual("/srv/Repo", result[2].Path);
    }

    private static RecentRepositoriesService CreateLocalService(MemorySettingsStore settings)
    {
        TestExecutionContextService context = new(
            new InMemoryRepositoryFileSystem(), RepositoryPathStyle.Windows, isLocal: true);
        return new RecentRepositoriesService(settings, new NullDiscoveryService(), context);
    }

    [TestMethod]
    public void Add_RemoteProfile_PersistsInProfileSpecificStorage()
    {
        MemorySettingsStore settings = new();
        TestExecutionContextService context = new(
            new InMemoryRepositoryFileSystem(),
            connectionProfileId: "server-one");
        RecentRepositoriesService service = new(settings, new NullDiscoveryService(), context);
        RepositoryInfo repository = new("/srv/repository", "repository", "main");

        IReadOnlyList<RepositoryInfo> repositories = service.Add(repository);

        Assert.HasCount(1, repositories);
        Assert.IsNull(settings.GetString("RecentRepositories"));
        Assert.IsNotNull(settings.GetString("RecentRepositories:server-one"));
    }

    [TestMethod]
    public void Load_DifferentRemoteProfiles_ReturnIndependentHistories()
    {
        MemorySettingsStore settings = new();
        settings.SetString(
            "RecentRepositories:server-one",
            JsonSerializer.Serialize(new[]
            {
                new RepositoryInfo("/srv/one", "one", "main")
            }));
        settings.SetString(
            "RecentRepositories:server-two",
            JsonSerializer.Serialize(new[]
            {
                new RepositoryInfo("/srv/two", "two", "main")
            }));

        RecentRepositoriesService first = CreateRemoteService(settings, "server-one");
        RecentRepositoriesService second = CreateRemoteService(settings, "server-two");

        Assert.AreEqual("/srv/one", first.Load().Single().Path);
        Assert.AreEqual("/srv/two", second.Load().Single().Path);
    }

    [TestMethod]
    public void Add_UnstoredRemoteConnection_DoesNotPersistHistory()
    {
        MemorySettingsStore settings = new();
        RecentRepositoriesService service = CreateRemoteService(settings, null);
        RepositoryInfo repository = new("/srv/repository", "repository", "main");

        IReadOnlyList<RepositoryInfo> repositories = service.Add(repository);

        Assert.HasCount(1, repositories);
        Assert.IsEmpty(settings.Values);
    }

    private static RecentRepositoriesService CreateRemoteService(
        MemorySettingsStore settings,
        string? profileId)
    {
        TestExecutionContextService context = new(
            new InMemoryRepositoryFileSystem(),
            connectionProfileId: profileId);
        return new RecentRepositoriesService(settings, new NullDiscoveryService(), context);
    }

    private sealed class NullDiscoveryService : IGitRepositoryDiscoveryService
    {
        public RepositoryInfo? TryOpenRepository(string path) => null;
    }

    private sealed class MemorySettingsStore : ILocalSettingsStore
    {
        public Dictionary<string, string> Values { get; } = [];
        public string? GetString(string key) => Values.GetValueOrDefault(key);
        public void SetString(string key, string value) => Values[key] = value;
    }
}
