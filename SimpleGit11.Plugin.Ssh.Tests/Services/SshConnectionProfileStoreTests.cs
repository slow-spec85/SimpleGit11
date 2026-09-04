using SimpleGit11.Services;
using SimpleGit11.Plugin.Ssh.Services;

namespace SimpleGit11.Plugin.Ssh.Tests.Services;

[TestClass]
public sealed class SshConnectionProfileStoreTests
{
    [TestMethod]
    public void Upsert_PersistsNonSecretFieldsAndUpdatesExistingProfile()
    {
        MemorySettingsStore settings = new();
        SshConnectionProfileStore store = new(settings);
        store.Upsert(new SshConnectionProfile(
            "profile-1", "old.example", 22, "git", null, "SHA256:old", DateTimeOffset.UtcNow.AddDays(-1)));
        store.Upsert(new SshConnectionProfile(
            "profile-1", "new.example", 2222, "user", @"C:\keys\id", "SHA256:new", DateTimeOffset.UtcNow));

        IReadOnlyList<SshConnectionProfile> profiles = store.Load();

        Assert.HasCount(1, profiles);
        Assert.AreEqual("new.example", profiles[0].Host);
        Assert.AreEqual(2222, profiles[0].Port);
        Assert.AreEqual("SHA256:new", profiles[0].ExpectedHostKey);
        Assert.IsFalse(settings.Value!.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Load_ProfileSavedBeforePluginExtraction_PreservesIdentityAndTrustedKey()
    {
        MemorySettingsStore settings = new()
        {
            Value = """
                [{"Id":"existing-profile","Host":"server.example","Port":2222,"Username":"git","PrivateKeyPath":"C:\\keys\\id","ExpectedHostKey":"SHA256:trusted","LastUsedAt":"2026-08-01T10:00:00+00:00"}]
                """
        };
        SshConnectionProfileStore store = new(settings);

        SshConnectionProfile profile = store.Load().Single();
        store.Upsert(profile);
        SshConnectionProfile reloaded = new SshConnectionProfileStore(settings).Load().Single();

        Assert.AreEqual("existing-profile", reloaded.Id);
        Assert.AreEqual("SHA256:trusted", reloaded.ExpectedHostKey);
        Assert.AreEqual(@"C:\keys\id", reloaded.PrivateKeyPath);
        Assert.AreEqual(profile, reloaded);
    }

    [TestMethod]
    public void Load_InvalidJson_ReturnsEmptyList()
    {
        MemorySettingsStore settings = new() { Value = "not-json" };
        SshConnectionProfileStore store = new(settings);

        Assert.IsEmpty(store.Load());
    }

    [TestMethod]
    public void Delete_RemovesOnlySelectedProfile()
    {
        MemorySettingsStore settings = new();
        SshConnectionProfileStore store = new(settings);
        store.Upsert(new SshConnectionProfile(
            "profile-1", "one.example", 22, "git", null, null, DateTimeOffset.UtcNow));
        store.Upsert(new SshConnectionProfile(
            "profile-2", "two.example", 22, "git", null, null, DateTimeOffset.UtcNow));

        store.Delete("profile-1");

        IReadOnlyList<SshConnectionProfile> profiles = store.Load();
        Assert.HasCount(1, profiles);
        Assert.AreEqual("profile-2", profiles[0].Id);
    }

    private sealed class MemorySettingsStore : ILocalSettingsStore
    {
        public string? Value { get; set; }
        public string? GetString(string key) => Value;
        public void SetString(string key, string value) => Value = value;
    }
}
