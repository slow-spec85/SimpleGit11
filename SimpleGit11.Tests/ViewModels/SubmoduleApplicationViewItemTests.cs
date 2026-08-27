using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class SubmoduleApplicationViewItemTests
{
    [TestMethod]
    public void Constructor_InitializedSubmodule_UsesShortPinnedAndLocalCommits()
    {
        GitSubmoduleApplicationState state = new(
            "External/TextControlBox",
            "C:\\repository",
            "External/TextControlBox",
            "1111111111111111111111111111111111111111",
            "2222222222222222222222222222222222222222",
            true);

        SubmoduleApplicationViewItem item = new(state, new TestLocalizationService());

        Assert.AreEqual("External/TextControlBox", item.Path);
        Assert.AreEqual("Pinned: 1111111 · Local: 2222222", item.Description);
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public AppLanguage CurrentLanguage => AppLanguage.English;

        public string GetString(string resourceKey)
        {
            return resourceKey switch
            {
                "SynchronizationSubmoduleApplicationDescription" => "Pinned: {0} · Local: {1}",
                "SynchronizationSubmoduleNotInitialized" => "not initialized",
                _ => resourceKey
            };
        }

        public void ApplyLanguage()
        {
        }

        public void SetLanguage(AppLanguage language)
        {
        }
    }
}
