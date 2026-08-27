using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class SubmoduleSynchronizationViewItemTests
{
    [TestMethod]
    public void Constructor_OutgoingChange_UsesRepositoryPathAndShortCommits()
    {
        GitSubmoduleReferenceChange change = new(
            "External/TextControlBox",
            "1111111111111111111111111111111111111111",
            "2222222222222222222222222222222222222222",
            GitSubmoduleReferenceChangeKind.Updated);

        SubmoduleSynchronizationViewItem item = new(
            change,
            "main",
            SubmoduleSynchronizationDirection.Outgoing,
            new TestLocalizationService());

        Assert.AreEqual("External/TextControlBox", item.Path);
        Assert.AreEqual(
            "Pinned version 1111111 → 2222222. It will be pushed with branch main.",
            item.Description);
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public AppLanguage CurrentLanguage => AppLanguage.English;

        public string GetString(string resourceKey)
        {
            return resourceKey switch
            {
                "SynchronizationSubmoduleOutgoingDescription" =>
                    "Pinned version {0} → {1}. It will be pushed with branch {2}.",
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
