using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class SettingsServiceTests
{
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

    [TestMethod]
    public void Constructor_EnablesPrereleasesForPrereleaseBuildByDefault()
    {
        MemoryLocalSettingsStore store = new();

        SettingsService service = CreateService(store, "1.2.0-preview.1");

        Assert.IsTrue(service.Current.IncludePrereleaseVersions);
        Assert.IsNull(store.GetString("IncludePrereleaseVersions"));
    }

    [TestMethod]
    public void Constructor_UsesPersistedPrereleasePreference()
    {
        MemoryLocalSettingsStore store = new();
        store.SetString("IncludePrereleaseVersions", "False");

        SettingsService service = CreateService(store, "1.2.0-preview.1");

        Assert.IsFalse(service.Current.IncludePrereleaseVersions);
    }

    [TestMethod]
    public void SetIncludePrereleaseVersions_PersistsValue()
    {
        MemoryLocalSettingsStore store = new();
        SettingsService service = CreateService(store);

        service.SetIncludePrereleaseVersions(true);

        Assert.IsTrue(service.Current.IncludePrereleaseVersions);
        Assert.AreEqual("True", store.GetString("IncludePrereleaseVersions"));
    }

    private static SettingsService CreateService(
        ILocalSettingsStore store,
        string currentVersion = "1.0.0")
    {
        return new SettingsService(store, new TestProductInfoService(currentVersion));
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
