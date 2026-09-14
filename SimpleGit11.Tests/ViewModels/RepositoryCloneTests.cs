using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Execution;
using SimpleGit11.Services.Git;
using SimpleGit11.Tests.TestInfrastructure;
using SimpleGit11.ViewModels;
using Stub = SimpleGit11.Tests.ViewModels.SettingsPullSettingsTests.ServiceStub;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class RepositoryCloneTests
{
    [TestMethod]
    public async Task CloneAsync_SshFailure_ShowsSettingsAction()
    {
        Fixture fixture = new("git@gitlab.com:owner/repository.git");
        AppNavigationTarget? navigationTarget = null;
        fixture.Window.NavigationRequested += (_, args) => navigationTarget = args.Target;

        await fixture.CloneAsync();

        Assert.AreEqual(InfoBarSeverity.Error, fixture.Window.NotificationSeverity);
        Assert.AreEqual("RemoteSshAccessAuthenticationFailed", fixture.Window.NotificationMessage);
        Assert.AreEqual(Fixture.FailureMessage, fixture.Window.NotificationDetails);
        Assert.AreEqual("OpenSshSettingsButton", fixture.Window.NotificationActionText);
        Assert.IsNotNull(fixture.Window.NotificationActionCommand);

        fixture.Window.NotificationActionCommand.Execute(null);

        Assert.AreEqual(AppNavigationTarget.Settings, navigationTarget);
    }

    [TestMethod]
    public async Task CloneAsync_HttpsFailure_DoesNotShowSettingsAction()
    {
        Fixture fixture = new("https://gitlab.com/owner/repository.git");

        await fixture.CloneAsync();

        Assert.AreEqual(InfoBarSeverity.Error, fixture.Window.NotificationSeverity);
        Assert.AreEqual("GitCloneCommandFailed", fixture.Window.NotificationMessage);
        Assert.AreEqual(Fixture.FailureMessage, fixture.Window.NotificationDetails);
        Assert.IsNull(fixture.Window.NotificationActionCommand);
        Assert.IsNull(fixture.Window.NotificationActionText);
    }

    [TestMethod]
    public async Task CloneAsync_CredentialManagerFailure_ShowsSpecificMessage()
    {
        const string failure = "Git Credential Manager failed. TLS certificate failure";
        Fixture fixture = new(
            "https://gitlab.example.test/owner/repository.git",
            failureMessage: failure);

        await fixture.CloneAsync();

        Assert.AreEqual(InfoBarSeverity.Error, fixture.Window.NotificationSeverity);
        Assert.AreEqual("CredentialManagerFailed", fixture.Window.NotificationMessage);
        Assert.AreEqual(failure, fixture.Window.NotificationDetails);
        Assert.IsNull(fixture.Window.NotificationActionCommand);
        Assert.IsNull(fixture.Window.NotificationActionText);
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task CloneAsync_CancelAction_CancelsGitAndClosesProgress()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Fixture fixture = new(
            "git@gitlab.com:owner/repository.git",
            async cancellationToken =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new AssertFailedException("Canceled clone must not continue.");
            });

        Task clone = fixture.CloneAsync();
        await started.Task;

        Assert.IsTrue(fixture.Window.IsOperationRunning);
        Assert.IsNotNull(fixture.Window.OperationCancelCommand);
        fixture.Window.OperationCancelCommand.Execute(null);
        await clone;

        Assert.IsFalse(fixture.Window.IsOperationRunning);
        Assert.IsNull(fixture.Window.OperationCancelCommand);
        Assert.AreEqual("RemoteOperationCanceled", fixture.Window.NotificationMessage);
    }

    private sealed class Fixture
    {
        public const string FailureMessage = "git@gitlab.com: Permission denied (publickey).";

        private readonly RepositoryViewModel _viewModel;

        public Fixture(
            string remoteUrl,
            Func<CancellationToken, Task<RepositoryInfo>>? clone = null,
            string failureMessage = FailureMessage)
        {
            IGitRepositoryOperationService repositoryOperations =
                Stub.Create<IGitRepositoryOperationService>((method, arguments) => method == "CloneAsync"
                    ? clone?.Invoke((CancellationToken)arguments![3]!)
                        ?? Task.FromException<RepositoryInfo>(new GitCommandException(failureMessage, 128))
                    : throw new NotSupportedException(method));
            IGitRepositorySearchService repositorySearch = Stub.Create<IGitRepositorySearchService>(
                (method, _) => method switch
                {
                    "LoadStartPath" => "",
                    "LoadFoundRepositories" => Array.Empty<RepositoryInfo>(),
                    "SaveStartPath" => null,
                    _ => throw new NotSupportedException(method)
                });
            IGitConfigService configuration = Stub.Create<IGitConfigService>((method, _) =>
                method == "GetUserNameAsync"
                    ? Task.FromResult("")
                    : throw new NotSupportedException(method));
            IGitService git = Stub.Create<IGitService>((method, arguments) => method switch
            {
                "get_RepositoryOperations" => repositoryOperations,
                "get_RepositorySearch" => repositorySearch,
                "get_Configuration" => configuration,
                "ExecuteAsync" => ((Func<Task>)arguments![0]!)(),
                _ => throw new NotSupportedException(method)
            });
            ILocalizationService localization = Stub.Create<ILocalizationService>(
                (method, arguments) => method == "GetString"
                    ? arguments![0]!
                    : throw new NotSupportedException(method));
            StrongReferenceMessenger messenger = new();
            Window = new MainWindowViewModel(
                Stub.Create<IRecentRepositoriesService>((method, _) => method == "Load"
                    ? Array.Empty<RepositoryInfo>()
                    : throw new NotSupportedException(method)),
                localization,
                git,
                Stub.Create<IClipboardService>(),
                new TestProductInfoService(),
                messenger,
                Stub.Create<ISettingsService>((method, _) => method == "get_Current"
                    ? new AppSettings()
                    : throw new NotSupportedException(method)));
            _viewModel = new RepositoryViewModel(
                Stub.Create<IStoragePickerService>((method, _) => method == "PickFolderAsync"
                    ? Task.FromResult<string?>("C:\\repositories")
                    : throw new NotSupportedException(method)),
                Stub.Create<IRecentRepositoriesService>(),
                git,
                localization,
                Stub.Create<IClipboardService>(),
                Stub.Create<IFileExplorerService>(),
                Stub.Create<IDialogService>(),
                Window,
                messenger,
                new ImmediateAsyncCommandExecutor(),
                new TestExecutionContextService(
                    new InMemoryRepositoryFileSystem(),
                    RepositoryPathStyle.Windows,
                    isLocal: true),
                Stub.Create<IExecutionRepositoryDiscoveryService>(),
                Stub.Create<IApplicationInstanceLauncher>());
            _viewModel.CloneRepositoryUrl = remoteUrl;
        }

        public MainWindowViewModel Window { get; }

        public Task CloneAsync() => _viewModel.CloneRepositoryCommand.ExecuteAsync(null);
    }

    private sealed class ImmediateAsyncCommandExecutor : IAsyncCommandExecutor
    {
        public Task ExecuteAsync(Func<Task> operation) => operation();
    }
}
