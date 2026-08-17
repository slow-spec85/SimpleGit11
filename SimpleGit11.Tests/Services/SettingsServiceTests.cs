using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class SettingsServiceTests
{
    [TestMethod]
    public void Constructor_LoadsAndClampsEditorLineSpacing()
    {
        MemoryLocalSettingsStore store = new();
        store.SetString("EditorLineSpacing", "99");

        SettingsService service = new(store);

        Assert.AreEqual(16, service.Current.EditorLineSpacing);
    }

    [TestMethod]
    public void Constructor_UsesDefaultForInvalidEditorLineSpacing()
    {
        MemoryLocalSettingsStore store = new();
        store.SetString("EditorLineSpacing", "invalid");

        SettingsService service = new(store);

        Assert.AreEqual(AppSettings.DefaultEditorLineSpacing, service.Current.EditorLineSpacing);
    }

    [TestMethod]
    public void SetEditorLineSpacing_PersistsValueAndRaisesAppearanceChanged()
    {
        MemoryLocalSettingsStore store = new();
        SettingsService service = new(store);
        int appearanceChangedCount = 0;
        service.EditorAppearanceChanged += (_, _) => appearanceChangedCount++;

        service.SetEditorLineSpacing(6);
        service.SetEditorLineSpacing(6);

        Assert.AreEqual(6, service.Current.EditorLineSpacing);
        Assert.AreEqual("6", store.GetString("EditorLineSpacing"));
        Assert.AreEqual(1, appearanceChangedCount);
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
