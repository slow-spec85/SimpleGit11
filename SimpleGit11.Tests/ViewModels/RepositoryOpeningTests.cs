using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Execution;
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

    [TestMethod]
    public void OpeningAnotherRepository_ClearsCachedBranchViewModelState()
    {
        StrongReferenceMessenger messenger = new();
        MainWindowViewModel window = CreateWindow(new AppSettings(), messenger);
        BranchesViewModel branches = new(
            window,
            null!,
            Stub.Create<IGitService>(),
            Stub.Create<ILocalizationService>((_, args) => args![0]),
            Stub.Create<IClipboardService>(),
            Stub.Create<IDialogService>(),
            messenger,
            Stub.Create<IAsyncCommandExecutor>(),
            Stub.Create<IExecutionContextService>());
        window.SetCurrentRepository(new RepositoryInfo("C:/first", "first", "main"), []);
        branches.Branches.Add(new GitBranch("first-branch", true, false, "", "", null));
        branches.RemoteBranches.Add(new GitBranch("origin/first-branch", false, true, "", "", null));

        window.SetCurrentRepository(new RepositoryInfo("C:/second", "second", "main"), []);

        Assert.IsEmpty(branches.Branches);
        Assert.IsEmpty(branches.RemoteBranches);
        Assert.IsEmpty(branches.FilteredBranches);
        Assert.IsNull(branches.SelectedBranch);
    }

    [TestMethod]
    public async Task OpeningAnotherRepository_IgnoresLateBranchRefreshFromPreviousRepository()
    {
        TaskCompletionSource<IReadOnlyList<GitBranch>> previousRepositoryBranches = new();
        StrongReferenceMessenger messenger = new();
        MainWindowViewModel window = CreateWindow(new AppSettings(), messenger);
        IGitConfigService configuration = Stub.Create<IGitConfigService>((method, _) =>
            method == "GetBranchDescriptionsAsync"
                ? Task.FromResult<IReadOnlyDictionary<string, string>>(
                    new Dictionary<string, string>())
                : throw new NotSupportedException(method));
        IGitService gitService = Stub.Create<IGitService>((method, args) => method switch
        {
            "ExecuteAsync" => ((Func<Task>)args![0]!)(),
            "GetRemotesAsync" => Task.FromResult<IReadOnlyList<GitRemote>>([]),
            "GetOperationStateAsync" => Task.FromResult(GitOperationState.None),
            "GetLocalBranchesAsync" => previousRepositoryBranches.Task,
            "GetRemoteBranchesAsync" => Task.FromResult<IReadOnlyList<GitBranch>>([]),
            "get_Configuration" => configuration,
            _ => throw new NotSupportedException(method)
        });
        BranchesViewModel branches = new(
            window,
            null!,
            gitService,
            Stub.Create<ILocalizationService>((_, args) => args![0]),
            Stub.Create<IClipboardService>(),
            Stub.Create<IDialogService>(),
            messenger,
            Stub.Create<IAsyncCommandExecutor>(),
            Stub.Create<IExecutionContextService>());
        window.SetCurrentRepository(new RepositoryInfo("C:/first", "first", "main"), []);

        Task staleRefresh = branches.RefreshBranchesLocalAsync();
        window.SetCurrentRepository(new RepositoryInfo("C:/second", "second", "main"), []);
        previousRepositoryBranches.SetResult(
            [new GitBranch("first-branch", true, false, "", "", null)]);
        await staleRefresh;

        Assert.IsEmpty(branches.Branches);
        Assert.IsEmpty(branches.FilteredBranches);
    }

    private static MainWindowViewModel CreateWindow(
        AppSettings settings,
        IMessenger? messenger = null)
    {
        messenger ??= new StrongReferenceMessenger();
        return new MainWindowViewModel(
            Stub.Create<IRecentRepositoriesService>((_, _) => Array.Empty<RepositoryInfo>()),
            Stub.Create<ILocalizationService>((_, args) => args![0]),
            Stub.Create<IGitService>((method, _) => method == "GetRemotesAsync"
                ? Task.FromResult<IReadOnlyList<GitRemote>>([new("origin", ".", "."), new("upstream", ".", ".")])
                : throw new NotSupportedException(method)),
            Stub.Create<IClipboardService>(),
            new TestProductInfoService(),
            messenger,
            Stub.Create<ISettingsService>((_, _) => settings));
    }
}
