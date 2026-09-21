using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class SettingsServiceTests
{
    [TestMethod]
    public void RepositoryOpening_DefaultsPreserveManualFetchAndOrigin()
    {
        SettingsService service = CreateService(new MemoryLocalSettingsStore());
        Assert.AreEqual("origin", service.Current.DefaultRemoteName);
        Assert.IsFalse(service.Current.FetchOnRepositoryOpen);
        Assert.AreEqual(8, service.Current.RecentRepositoriesCount);
        Assert.IsFalse(service.Current.OpenLastRepositoryOnStartup);
    }

    [TestMethod]
    public void RepositoryOpening_PreferencesSurviveReload()
    {
        MemoryLocalSettingsStore store = new();
        SettingsService service = CreateService(store);
        service.SetDefaultRemoteName(" upstream ");
        service.SetFetchOnRepositoryOpen(true);
        service.SetRecentRepositoriesCount(12);
        service.SetOpenLastRepositoryOnStartup(true);

        SettingsService reloaded = CreateService(store);
        Assert.AreEqual("upstream", reloaded.Current.DefaultRemoteName);
        Assert.IsTrue(reloaded.Current.FetchOnRepositoryOpen);
        Assert.AreEqual(12, reloaded.Current.RecentRepositoriesCount);
        Assert.IsTrue(reloaded.Current.OpenLastRepositoryOnStartup);

        reloaded.SetDefaultRemoteName("  ");
        reloaded.SetFetchOnRepositoryOpen(false);
        SettingsService reset = CreateService(store);
        Assert.AreEqual("origin", reset.Current.DefaultRemoteName);
        Assert.IsFalse(reset.Current.FetchOnRepositoryOpen);
    }

    [TestMethod]
    public void RepositoryOpening_InvalidStoredValuesUseDefaults()
    {
        MemoryLocalSettingsStore store = new();
        store.SetString("DefaultRemoteName", " ");
        store.SetString("FetchOnRepositoryOpen", "invalid");
        store.SetString("RecentRepositoriesCount", "invalid");
        store.SetString("OpenLastRepositoryOnStartup", "invalid");
        SettingsService service = CreateService(store);
        Assert.AreEqual("origin", service.Current.DefaultRemoteName);
        Assert.IsFalse(service.Current.FetchOnRepositoryOpen);
        Assert.AreEqual(8, service.Current.RecentRepositoriesCount);
        Assert.IsFalse(service.Current.OpenLastRepositoryOnStartup);
    }

    [TestMethod]
    public void RecentRepositoriesCount_IsClampedWhenSavedAndLoaded()
    {
        MemoryLocalSettingsStore store = new();
        SettingsService service = CreateService(store);

        service.SetRecentRepositoriesCount(100);
        Assert.AreEqual(50, CreateService(store).Current.RecentRepositoriesCount);

        service.SetRecentRepositoriesCount(0);
        Assert.AreEqual(1, CreateService(store).Current.RecentRepositoriesCount);
    }

    [TestMethod]
    public void Constructor_LoadsAndClampsEditorLineSpacing()
    {
        MemoryLocalSettingsStore store = new();
        store.SetString("EditorLineSpacing", "99");

        SettingsService service = CreateService(store);

        Assert.AreEqual(16, service.Current.EditorLineSpacing);
    }

    [TestMethod]
    public void Constructor_UsesDefaultForInvalidEditorLineSpacing()
    {
        MemoryLocalSettingsStore store = new();
        store.SetString("EditorLineSpacing", "invalid");

        SettingsService service = CreateService(store);

        Assert.AreEqual(AppSettings.DefaultEditorLineSpacing, service.Current.EditorLineSpacing);
    }

    [TestMethod]
    public void SetEditorLineSpacing_PersistsValueAndRaisesAppearanceChanged()
    {
        MemoryLocalSettingsStore store = new();
        SettingsService service = CreateService(store);
        int appearanceChangedCount = 0;
        service.EditorAppearanceChanged += (_, _) => appearanceChangedCount++;

        service.SetEditorLineSpacing(6);
        service.SetEditorLineSpacing(6);

        Assert.AreEqual(6, service.Current.EditorLineSpacing);
        Assert.AreEqual("6", store.GetString("EditorLineSpacing"));
        Assert.AreEqual(1, appearanceChangedCount);
    }

    private static SettingsService CreateService(ILocalSettingsStore store)
    {
        return new SettingsService(store);
    }

    private sealed class MemoryLocalSettingsStore : ILocalSettingsStore
    {
        private readonly Dictionary<string, string> _values = [];

        public string? GetString(string key)
        {
            return _values.GetValueOrDefault(key);
        }

        public void SetString(string key, string value)
        {
            _values[key] = value;
        }
    }

}
