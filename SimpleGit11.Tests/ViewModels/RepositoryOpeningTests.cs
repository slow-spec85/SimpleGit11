using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using SimpleGit11.Tests.TestInfrastructure;
using SimpleGit11.ViewModels;
using Stub = SimpleGit11.Tests.ViewModels.SettingsPullSettingsTests.ServiceStub;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class RepositoryOpeningTests
{
    [TestMethod]
    public async Task RemoteSelection_UsesPreferenceAndPreservesExplicitSelectionUntilReopening()
    {
        AppSettings settings = new() { DefaultRemoteName = "upstream" };
        MainWindowViewModel window = CreateWindow(settings);
        RepositoryInfo repository = new("C:/repo", "repo", "main");
        window.SetCurrentRepository(repository, []);
        await window.RefreshRemotesAsync();
        Assert.AreEqual("upstream", window.SelectedRemoteName);

        window.SelectRemote("origin");
        await window.RefreshRemotesAsync();
        Assert.AreEqual("origin", window.SelectedRemoteName);

        window.SetCurrentRepository(repository, []);
        await window.RefreshRemotesAsync();
        Assert.AreEqual("upstream", window.SelectedRemoteName);
    }

    [TestMethod]
    public void RemoteSelection_MissingPreferenceFallsBackToOriginThenFirstThenNone()
    {
        MainWindowViewModel window = CreateWindow(new AppSettings { DefaultRemoteName = "missing" });
        GitRemote backup = new("backup", ".", ".");
        GitRemote origin = new("origin", ".", ".");
        Assert.AreSame(origin, window.ResolveSelectedRemote([backup, origin], null));
        Assert.AreSame(backup, window.ResolveSelectedRemote([backup], null));
        Assert.IsNull(window.ResolveSelectedRemote([], null));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Opening_NavigatesAndRequestsExactlyOneFetchOnlyWhenEnabled(bool enabled)
    {
        MainWindowViewModel window = CreateWindow(new AppSettings { FetchOnRepositoryOpen = enabled });
        RepositoryInfo repository = new("C:/repo", "repo", "main");
        int navigationCount = 0;
        window.NavigationRequested += (_, args) =>
        {
            Assert.AreEqual(AppNavigationTarget.Synchronization, args.Target);
            navigationCount++;
        };
        window.SetCurrentRepository(repository, []);
        Assert.IsFalse(window.TryConsumeOpeningFetch());
        window.CompleteRepositoryOpen(repository);
        Assert.AreEqual(enabled ? 1 : 0, navigationCount);
        Assert.AreEqual(enabled, window.TryConsumeOpeningFetch());
        Assert.IsFalse(window.TryConsumeOpeningFetch());
    }

    [TestMethod]
    public void Opening_ChangingOrClosingRepositoryDiscardsPendingFetchAndIgnoresStaleCompletion()
    {
        MainWindowViewModel window = CreateWindow(new AppSettings { FetchOnRepositoryOpen = true });
        RepositoryInfo first = new("C:/first", "first", "main");
        RepositoryInfo second = new("C:/second", "second", "main");
        window.SetCurrentRepository(first, []);
        window.CompleteRepositoryOpen(first);
        window.SetCurrentRepository(second, []);
        window.CompleteRepositoryOpen(first);
        Assert.IsFalse(window.TryConsumeOpeningFetch());
        window.CompleteRepositoryOpen(second);
        window.CloseCurrentRepository();
        Assert.IsFalse(window.TryConsumeOpeningFetch());
    }

    private static MainWindowViewModel CreateWindow(AppSettings settings)
    {
        return new MainWindowViewModel(
            Stub.Create<IRecentRepositoriesService>((_, _) => Array.Empty<RepositoryInfo>()),
            Stub.Create<ILocalizationService>((_, args) => args![0]),
            Stub.Create<IGitService>((method, _) => method == "GetRemotesAsync"
                ? Task.FromResult<IReadOnlyList<GitRemote>>([new("origin", ".", "."), new("upstream", ".", ".")])
                : throw new NotSupportedException(method)),
            Stub.Create<IClipboardService>(),
            new TestProductInfoService(),
            new StrongReferenceMessenger(),
            Stub.Create<ISettingsService>((_, _) => settings));
    }
}
