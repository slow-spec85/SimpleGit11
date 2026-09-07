using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Presentation.Navigation;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class NavigationSelectionResolverTests
{
    private static readonly IReadOnlySet<Type> TopLevelPageTypes = new HashSet<Type>
    {
        typeof(RepositoryPage),
        typeof(ChangesPage),
        typeof(SettingsPage)
    };

    [TestMethod]
    public void ResolveTopLevelPage_CurrentTopLevelPageWinsOverHistory()
    {
        Type? result = NavigationSelectionResolver.ResolveTopLevelPage(
            typeof(SettingsPage),
            [typeof(RepositoryPage), typeof(ChangesPage)],
            TopLevelPageTypes);

        Assert.AreEqual(typeof(SettingsPage), result);
    }

    [TestMethod]
    public void ResolveTopLevelPage_NestedPageUsesMostRecentTopLevelPage()
    {
        Type? result = NavigationSelectionResolver.ResolveTopLevelPage(
            typeof(CommitRangePage),
            [typeof(RepositoryPage), typeof(NestedPage), typeof(ChangesPage)],
            TopLevelPageTypes);

        Assert.AreEqual(typeof(ChangesPage), result);
    }

    [TestMethod]
    public void ResolveTopLevelPage_NestedPageWithoutOwnerReturnsNull()
    {
        Type? result = NavigationSelectionResolver.ResolveTopLevelPage(
            typeof(CommitRangePage),
            [typeof(NestedPage)],
            TopLevelPageTypes);

        Assert.IsNull(result);
    }

    private sealed class RepositoryPage
    { }

    private sealed class ChangesPage
    { }

    private sealed class SettingsPage
    { }

    private sealed class CommitRangePage
    { }

    private sealed class NestedPage
    { }
}
