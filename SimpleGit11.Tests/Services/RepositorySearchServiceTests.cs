using System.Threading;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Execution;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class RepositorySearchServiceTests
{
    [TestMethod]
    public async Task SearchAsync_RemoteTree_FindsGitDirectoriesAndFiles()
    {
        TreeFileSystem files = new();
        files.AddDirectory("/srv", "/srv/Repo", "/srv/group");
        files.AddDirectory("/srv/Repo");
        files.AddDirectory("/srv/Repo/.git");
        files.AddDirectory("/srv/group", "/srv/group/nested");
        files.AddDirectory("/srv/group/nested");
        files.AddFile("/srv/group/nested/.git");
        TestExecutionContextService context = new(
            files,
            connectionProfileId: "server-one");
        StubExecutionDiscoveryService discovery = new();
        RepositorySearchService service = new(
            new NullLocalDiscoveryService(),
            discovery,
            new MemorySettingsStore(),
            context);

        IReadOnlyList<RepositoryInfo> repositories = await service.SearchAsync("/srv");

        CollectionAssert.AreEquivalent(
            new[] { "/srv/Repo", "/srv/group/nested" },
            repositories.Select(repository => repository.Path).ToArray());
    }

    [TestMethod]
    public void SearchSettings_RemoteProfile_AreStoredSeparately()
    {
        TreeFileSystem files = new();
        MemorySettingsStore settings = new();
        TestExecutionContextService context = new(
            files,
            connectionProfileId: "server-one");
        RepositorySearchService service = new(
            new NullLocalDiscoveryService(),
            new StubExecutionDiscoveryService(),
            settings,
            context);

        service.SaveStartPath("/srv");

        Assert.AreEqual("/srv", settings.GetString("RepositorySearchStartPath:server-one"));
        Assert.IsNull(settings.GetString("RepositorySearchStartPath"));
    }

    private sealed class StubExecutionDiscoveryService : IExecutionRepositoryDiscoveryService
    {
        public Task<RepositoryInfo?> TryOpenRepositoryAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            string name = path.Split('/')[^1];
            return Task.FromResult<RepositoryInfo?>(new RepositoryInfo(path, name, "main"));
        }
    }

    private sealed class NullLocalDiscoveryService : IGitRepositoryDiscoveryService
    {
        public RepositoryInfo? TryOpenRepository(string path) => null;
    }

    private sealed class TreeFileSystem : IRepositoryFileSystem
    {
        private readonly Dictionary<string, IReadOnlyList<string>> _directories = new(StringComparer.Ordinal);
        private readonly HashSet<string> _files = new(StringComparer.Ordinal);

        public void AddDirectory(string path, params string[] children) => _directories[path] = children;
        public void AddFile(string path) => _files.Add(path);
        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(_files.Contains(path));
        public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(_directories.ContainsKey(path));
        public Task<IReadOnlyList<string>> EnumerateDirectoriesAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(_directories.GetValueOrDefault(path, []));
        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task WriteAllBytesAtomicAsync(string path, byte[] content, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<RepositoryFileMetadata?> GetMetadataAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<RepositoryFileMetadata?>(null);
    }

    private sealed class MemorySettingsStore : ILocalSettingsStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? GetString(string key) => _values.GetValueOrDefault(key);
        public void SetString(string key, string value) => _values[key] = value;
    }
}
