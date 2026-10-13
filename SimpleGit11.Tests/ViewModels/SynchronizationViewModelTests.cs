using CommunityToolkit.Mvvm.Messaging;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using SimpleGit11.Tests.TestInfrastructure;
using SimpleGit11.ViewModels;
using Stub = SimpleGit11.Tests.ViewModels.SettingsPullSettingsTests.ServiceStub;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class SynchronizationViewModelTests
{
    private static Func<string, Guid, IntPtr>? _previousActivationHandler;

    [DllImport("CoreMessagingXP.dll", ExactSpelling = true)]
    private static extern int DllGetActivationFactory(IntPtr className, out IntPtr factory);

    [ClassInitialize]
    public static void InitializeRuntime(TestContext context)
    {
        // Activate the self-contained dispatcher without the app executable's manifest.
        _previousActivationHandler = WinRT.ActivationFactory.ActivationHandler;
        WinRT.ActivationFactory.ActivationHandler = (className, interfaceId) =>
        {
            if (!className.StartsWith("Microsoft.UI.Dispatching.", StringComparison.Ordinal))
            {
                return _previousActivationHandler?.Invoke(className, interfaceId) ?? IntPtr.Zero;
            }

            WinRT.MarshalString name = WinRT.MarshalString.CreateMarshaler(className);
            IntPtr factory = IntPtr.Zero;
            try
            {
                Marshal.ThrowExceptionForHR(DllGetActivationFactory(name.GetAbi(), out factory));
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(factory, ref interfaceId, out IntPtr result));
                return result;
            }
            finally
            {
                if (factory != IntPtr.Zero)
                {
                    Marshal.Release(factory);
                }
                name.Dispose();
            }
        };
    }

    [ClassCleanup]
    public static void CleanupRuntime() => WinRT.ActivationFactory.ActivationHandler = _previousActivationHandler;

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task PullIsAvailableWithoutFetchOrKnownIncomingCommits(bool hasUpstream)
    {
        await using Fixture fixture = new() { HasUpstream = hasUpstream };
        await fixture.InitializeAsync();

        Assert.AreEqual(0, fixture.ViewModel.Snapshot!.CurrentBranch!.BehindCount);
        Assert.IsTrue(fixture.ViewModel.CanPull);
        Assert.IsTrue(fixture.ViewModel.PullCommand.CanExecute(null));

        await fixture.ViewModel.PullCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "pull", "snapshot" }, fixture.Operations);
        Assert.AreEqual(hasUpstream ? 2 : 4, fixture.PullArgumentCount);
        Assert.IsTrue(fixture.ViewModel.PullCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task PullIsDisabledWithoutCurrentBranchOrRemote()
    {
        await using Fixture fixture = new();
        Assert.IsFalse(fixture.ViewModel.PullCommand.CanExecute(null));
        fixture.HasCurrentBranch = false;
        await fixture.InitializeAsync();
        Assert.IsFalse(fixture.ViewModel.PullCommand.CanExecute(null));
        fixture.ViewModel.SelectedRemote = null;
        Assert.IsFalse(fixture.ViewModel.PullCommand.CanExecute(null));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task SynchronizePullsBeforePushingUsingRefreshedSnapshot(bool hasUpstream)
    {
        await using Fixture fixture = new() { HasUpstream = hasUpstream };
        await fixture.InitializeAsync();

        await fixture.ViewModel.SynchronizeCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "pull", "snapshot", "push", "snapshot" }, fixture.Operations);
        Assert.AreEqual(hasUpstream ? 2 : 4, fixture.PullArgumentCount);
        Assert.IsTrue(fixture.ViewModel.SynchronizeCommand.CanExecute(null));
    }

    [TestMethod]
    [DataRow("pull")]
    [DataRow("cancel")]
    [DataRow("snapshot")]
    public async Task SynchronizeDoesNotPushAfterPullFailureCancellationOrRefreshFailure(string failure)
    {
        await using Fixture fixture = new() { Failure = failure };
        await fixture.InitializeAsync();

        await fixture.ViewModel.SynchronizeCommand.ExecuteAsync(null);

        Assert.IsFalse(fixture.Operations.Contains("push"));
        Assert.IsFalse(fixture.ViewModel.IsGitOperationRunning);
    }

    [TestMethod]
    public async Task SynchronizeSkipsPushWhenOnlyOtherBranchesAndTagsHaveOutgoingChanges()
    {
        await using Fixture fixture = new() { HasOutgoingAfterPull = false };
        await fixture.InitializeAsync();

        await fixture.ViewModel.SynchronizeCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "pull", "snapshot" }, fixture.Operations);
    }

    [TestMethod]
    public async Task SynchronizeIsDisabledWithoutCurrentBranchOrRemote()
    {
        await using Fixture fixture = new();
        Assert.IsFalse(fixture.ViewModel.SynchronizeCommand.CanExecute(null));
        fixture.HasCurrentBranch = false;
        await fixture.InitializeAsync();
        Assert.IsFalse(fixture.ViewModel.SynchronizeCommand.CanExecute(null));
        fixture.ViewModel.SelectedRemote = null;
        Assert.IsFalse(fixture.ViewModel.SynchronizeCommand.CanExecute(null));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly GitRemote _remote = new("origin", "https://example.test/repo", "https://example.test/repo");
        private bool _pulled;
        private readonly DispatcherQueueController _queue = DispatcherQueueController.CreateOnDedicatedThread();
        public SynchronizationViewModel ViewModel { get; }
        public List<string> Operations { get; } = [];
        public bool HasUpstream { get; init; } = true;
        public bool HasOutgoingAfterPull { get; init; } = true;
        public bool HasCurrentBranch { get; set; } = true;
        public string? Failure { get; init; }
        public int PullArgumentCount { get; private set; }

        public Fixture()
        {
            StrongReferenceMessenger messenger = new();
            ILocalizationService strings = Stub.Create<ILocalizationService>((method, args) =>
                method == "get_CurrentLanguage" ? AppLanguage.English : args![0] + " {0}");
            IGitRemoteService remotes = Stub.Create<IGitRemoteService>((method, args) => method switch
            {
                "GetConfiguredSynchronizationSnapshotAsync" => GetSnapshotAsync(),
                "PullAsync" => PullAsync(args!),
                "PushAsync" => PushAsync((GitPushRequest)args![1]!),
                _ => throw new NotSupportedException(method)
            });
            IGitSubmoduleService submodules = Stub.Create<IGitSubmoduleService>((method, _) => method switch
            {
                "GetApplicationStatesAsync" => Task.FromResult<IReadOnlyList<GitSubmoduleApplicationState>>([]),
                "GetReferenceChangesAsync" => Task.FromResult<IReadOnlyList<GitSubmoduleReferenceChange>>([]),
                _ => throw new NotSupportedException(method)
            });
            IGitService git = Stub.Create<IGitService>((method, args) => method switch
            {
                "get_Remotes" => remotes,
                "get_Submodules" => submodules,
                "ExecuteAsync" => ((Func<Task>)args![0]!)(),
                "GetRemotesAsync" => Task.FromResult<IReadOnlyList<GitRemote>>([_remote]),
                "GetLastFetchTimeAsync" => Task.FromResult<DateTimeOffset?>(null),
                _ => throw new NotSupportedException(method)
            });
            MainWindowViewModel window = new(
                Stub.Create<IRecentRepositoriesService>((_, _) => Array.Empty<RepositoryInfo>()), strings, git,
                Stub.Create<IClipboardService>(), new TestProductInfoService(), messenger,
                Stub.Create<ISettingsService>((_, _) => new AppSettings()));
            window.SetCurrentRepository(new RepositoryInfo(Environment.CurrentDirectory, "repo", "main"), []);
            ViewModel = new SynchronizationViewModel(window, git, strings, Stub.Create<IClipboardService>(),
                Stub.Create<IDialogService>(), messenger,
                Stub.Create<IAsyncCommandExecutor>((_, args) => ((Func<Task>)args![0]!)()));
        }

        public async ValueTask DisposeAsync() => await _queue.ShutdownQueueAsync();

        public async Task InitializeAsync()
        {
            ViewModel.InitializeDispatcherQueue(_queue.DispatcherQueue);
            await ViewModel.RefreshSynchronizationLocalAsync();
            Operations.Clear();
        }

        private Task<SynchronizationSnapshot> GetSnapshotAsync()
        {
            Operations.Add("snapshot");
            if (_pulled && Failure == "snapshot")
            {
                throw new DirectoryNotFoundException("snapshot failed");
            }

            int ahead = _pulled && HasOutgoingAfterPull ? 1 : 0;
            BranchSynchronizationItem branch = new("main", HasCurrentBranch,
                HasUpstream ? "origin/main" : "", "origin", "=", "origin", "", "origin/main", ">",
                ahead, 0, false, "origin/main", true, true, ahead, 0);
            BranchSynchronizationItem otherBranch = new("feature", false,
                "origin/feature", "origin", "=", "origin", "", "origin/feature", ">",
                1, 0, false, "origin/feature", true, true, 1, 0);
            TagSynchronizationItem tag = new(new GitTag("v1", false, false, "hash", "", null), "", "");
            return Task.FromResult(new SynchronizationSnapshot(_remote, [branch, otherBranch], [tag]));
        }

        private Task<GitRemoteOperationResult> PullAsync(object?[] args)
        {
            Operations.Add("pull");
            PullArgumentCount = args.Length;
            Assert.IsFalse(ViewModel.SynchronizeCommand.CanExecute(null));
            Assert.IsFalse(ViewModel.PullCommand.CanExecute(null));
            if (Failure == "pull")
            {
                throw new DirectoryNotFoundException("pull failed");
            }
            if (Failure == "cancel")
            {
                ViewModel.CancelRemoteOperationCommand.Execute(null);
                ((CancellationToken)args[^1]!).ThrowIfCancellationRequested();
            }
            _pulled = true;
            return Task.FromResult(new GitRemoteOperationResult("pulled"));
        }

        private Task<GitRemoteOperationResult> PushAsync(GitPushRequest request)
        {
            Operations.Add("push");
            Assert.IsTrue(_pulled);
            Assert.HasCount(1, request.References);
            Assert.AreEqual(GitPushReferenceKind.Branch, request.References[0].Kind);
            Assert.AreEqual("main", request.References[0].Name);
            return Task.FromResult(new GitRemoteOperationResult("pushed"));
        }
    }
}
