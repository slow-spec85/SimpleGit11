using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Extensibility.Plugins;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Tests.TestInfrastructure;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class AboutDialogViewModelTests
{
    [TestMethod]
    public async Task LoadAsync_NewerStableRelease_AllowsUpdate()
    {
        ProductReleaseInfo release = CreateRelease("1.2.3");
        TestProductInfoService productInfoService = new() { LatestRelease = release };
        TestProductUpdateService updateService = new() { Available = true };
        using AboutDialogViewModel viewModel = CreateViewModel(
            productInfoService,
            updateService,
            new TestInstallerLauncher());
        List<string> updateButtonLabels = [];
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.InstallUpdateButtonText))
            {
                updateButtonLabels.Add(viewModel.InstallUpdateButtonText);
            }
        };

        await viewModel.LoadAsync();

        Assert.IsTrue(viewModel.HasLatestRelease);
        Assert.AreEqual("1.2.3", viewModel.LatestReleaseVersion);
        Assert.IsNotNull(viewModel.LatestReleaseUri);
        Assert.IsTrue(viewModel.CanInstallUpdate);
        CollectionAssert.AreEqual(new[] { "AboutInstallUpdateButtonText" }, updateButtonLabels);
        Assert.IsFalse(viewModel.HasReleaseStatus);
        Assert.AreEqual(1, productInfoService.ReleaseRequests);
        Assert.AreEqual(release, updateService.AvailabilityRelease);
        Assert.AreEqual("1.0.0", updateService.AvailabilityCurrentVersion);
        Assert.IsFalse(viewModel.HasSshPlugin);
        Assert.AreEqual(string.Empty, viewModel.SshPluginVersion);
    }

    [TestMethod]
    public async Task LoadAsync_CurrentRelease_DisallowsUpdate()
    {
        TestProductUpdateService updateService = new() { Available = false };
        using AboutDialogViewModel viewModel = CreateViewModel(
            new TestProductInfoService { LatestRelease = CreateRelease("1.0.0") },
            updateService,
            new TestInstallerLauncher());

        await viewModel.LoadAsync();

        Assert.IsTrue(viewModel.HasLatestRelease);
        Assert.IsFalse(viewModel.CanInstallUpdate);
        Assert.AreEqual("", viewModel.InstallUpdateButtonText);
    }

    [TestMethod]
    public async Task InstallUpdateAsync_DownloadsAndLaunchesInstaller()
    {
        ProductReleaseInfo release = CreateRelease("1.2.3");
        TestProductUpdateService updateService = new()
        {
            Available = true,
            InstallerPath = @"C:\Temp\SimpleGit11-1.2.3-win-x64-setup.exe"
        };
        TestInstallerLauncher launcher = new();
        StrongReferenceMessenger messenger = new();
        List<AppOperationMessage> operations = [];
        messenger.Register<AppOperationMessage>(operations, static (recipient, message) =>
            ((List<AppOperationMessage>)recipient).Add(message));
        using AboutDialogViewModel viewModel = CreateViewModel(
            new TestProductInfoService { LatestRelease = release },
            updateService,
            launcher,
            messenger: messenger);
        await viewModel.LoadAsync();

        bool installerLaunched = await viewModel.InstallUpdateAsync();

        Assert.AreEqual(release, updateService.DownloadedRelease);
        Assert.AreEqual(updateService.InstallerPath, launcher.InstallerPath);
        Assert.IsTrue(installerLaunched);
        Assert.IsFalse(viewModel.IsDownloadingUpdate);
        Assert.IsTrue(operations[0].IsRunning);
        Assert.AreEqual("AboutUpdateValidateRelease…", operations[0].Message);
        Assert.IsTrue(operations[^2].IsRunning);
        Assert.AreEqual("AboutUpdateLaunchInstaller…", operations[^2].Message);
        Assert.IsFalse(operations[^1].IsRunning);
        Assert.IsTrue(operations.All(operation => ReferenceEquals(viewModel, operation.Source)));
    }

    [TestMethod]
    public async Task InstallUpdateAsync_DownloadingViaApi_ReportsFallbackToGlobalProgress()
    {
        TaskCompletionSource<string> download = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TestProductUpdateService updateService = new()
        {
            Available = true,
            DownloadStage = ProductUpdateStage.DownloadChecksumViaApi,
            DownloadTask = download.Task
        };
        StrongReferenceMessenger messenger = new();
        List<AppOperationMessage> operations = [];
        messenger.Register<AppOperationMessage>(operations, static (recipient, message) =>
            ((List<AppOperationMessage>)recipient).Add(message));
        using AboutDialogViewModel viewModel = CreateViewModel(
            new TestProductInfoService { LatestRelease = CreateRelease("1.2.3") },
            updateService,
            new TestInstallerLauncher(),
            messenger: messenger);
        await viewModel.LoadAsync();

        Task<bool> update = viewModel.InstallUpdateAsync();
        bool isDownloading = viewModel.IsDownloadingUpdate;
        AppOperationMessage progress = operations[^1];
        download.SetResult(updateService.InstallerPath);
        await update;

        Assert.IsTrue(isDownloading);
        Assert.IsTrue(progress.IsRunning);
        Assert.AreEqual("AboutUpdateDownloadChecksumViaApi…", progress.Message);
    }

    [TestMethod]
    [DataRow(ProductUpdateStage.DownloadChecksum, "AboutUpdateDownloadChecksum")]
    [DataRow(ProductUpdateStage.DownloadChecksumViaApi, "AboutUpdateDownloadChecksumViaApi")]
    [DataRow(ProductUpdateStage.DownloadInstallerViaApi, "AboutUpdateDownloadInstallerViaApi")]
    [DataRow(ProductUpdateStage.VerifyInstaller, "AboutUpdateVerifyInstaller")]
    [DataRow(ProductUpdateStage.SaveInstaller, "AboutUpdateSaveInstaller")]
    public async Task InstallUpdateAsync_DownloadFails_ShowsFailedStageAndDoesNotLaunch(
        ProductUpdateStage stage,
        string expectedStageKey)
    {
        TestProductUpdateService updateService = new()
        {
            Available = true,
            DownloadStage = stage,
            DownloadTask = Task.FromException<string>(new IOException("Update failure"))
        };
        TestInstallerLauncher launcher = new();
        StrongReferenceMessenger messenger = new();
        List<AppOperationMessage> operations = [];
        messenger.Register<AppOperationMessage>(operations, static (recipient, message) =>
            ((List<AppOperationMessage>)recipient).Add(message));
        List<AppNotificationMessage> notifications = [];
        messenger.Register<AppNotificationMessage>(notifications, static (recipient, message) =>
            ((List<AppNotificationMessage>)recipient).Add(message));
        using AboutDialogViewModel viewModel = CreateViewModel(
            new TestProductInfoService { LatestRelease = CreateRelease("1.2.3") },
            updateService,
            launcher,
            messenger: messenger);
        await viewModel.LoadAsync();

        bool installerLaunched = await viewModel.InstallUpdateAsync();

        Assert.IsFalse(installerLaunched);
        Assert.IsNull(launcher.InstallerPath);
        Assert.IsFalse(viewModel.IsDownloadingUpdate);
        Assert.IsFalse(operations[^1].IsRunning);
        AppNotificationMessage notification = notifications.Single();
        Assert.AreEqual(AppNotificationSeverity.Error, notification.Severity);
        Assert.AreEqual($"AboutUpdateFailed ({expectedStageKey})", notification.Message);
    }

    [TestMethod]
    public async Task InstallUpdateAsync_LaunchFails_ShowsLaunchStageFailure()
    {
        TestInstallerLauncher launcher = new() { LaunchException = new InvalidOperationException() };
        StrongReferenceMessenger messenger = new();
        List<AppNotificationMessage> notifications = [];
        messenger.Register<AppNotificationMessage>(notifications, static (recipient, message) =>
            ((List<AppNotificationMessage>)recipient).Add(message));
        using AboutDialogViewModel viewModel = CreateViewModel(
            new TestProductInfoService { LatestRelease = CreateRelease("1.2.3") },
            new TestProductUpdateService { Available = true },
            launcher,
            messenger: messenger);
        await viewModel.LoadAsync();

        bool installerLaunched = await viewModel.InstallUpdateAsync();

        Assert.IsFalse(installerLaunched);
        AppNotificationMessage notification = notifications.Single();
        Assert.AreEqual(AppNotificationSeverity.Error, notification.Severity);
        Assert.AreEqual("AboutUpdateFailed (AboutUpdateLaunchInstaller)", notification.Message);
    }

    [TestMethod]
    public async Task LoadAsync_RequestFails_ShowsLocalizedErrorState()
    {
        TestProductInfoService productInfoService = new()
        {
            ReleaseException = new HttpRequestException("Unavailable")
        };
        using AboutDialogViewModel viewModel = CreateViewModel(
            productInfoService,
            new TestProductUpdateService(),
            new TestInstallerLauncher());

        await viewModel.LoadAsync();

        Assert.IsTrue(viewModel.HasReleaseError);
        Assert.IsTrue(viewModel.HasReleaseStatus);
        Assert.AreEqual("AboutReleaseCheckFailed", viewModel.ReleaseStatusMessage);
    }

    [TestMethod]
    public void Constructor_SshPluginInstalled_ExposesItsVersion()
    {
        TestPluginCatalog plugins = new([
            new PluginMetadata("test.z", "Zulu", "2.0.0", "1.0"),
            new PluginMetadata("simplegit11.ssh", "SSH", "1.0.0", "1.0")
        ]);
        using AboutDialogViewModel viewModel = CreateViewModel(
            new TestProductInfoService(),
            new TestProductUpdateService(),
            new TestInstallerLauncher(),
            plugins);

        Assert.IsTrue(viewModel.HasSshPlugin);
        Assert.AreEqual("1.0.0", viewModel.SshPluginVersion);
    }

    [TestMethod]
    public void Constructor_OnlyOtherPluginInstalled_HidesSshPluginVersion()
    {
        TestPluginCatalog plugins = new([
            new PluginMetadata("test.other", "Other", "2.0.0", "1.0")
        ]);
        using AboutDialogViewModel viewModel = CreateViewModel(
            new TestProductInfoService(),
            new TestProductUpdateService(),
            new TestInstallerLauncher(),
            plugins);

        Assert.IsFalse(viewModel.HasSshPlugin);
        Assert.AreEqual(string.Empty, viewModel.SshPluginVersion);
    }

    private static AboutDialogViewModel CreateViewModel(
        TestProductInfoService productInfoService,
        TestProductUpdateService updateService,
        TestInstallerLauncher launcher,
        IPluginCatalog? plugins = null,
        IMessenger? messenger = null)
    {
        return new AboutDialogViewModel(
            productInfoService,
            updateService,
            launcher,
            new TestLocalizationService(),
            plugins ?? new TestPluginCatalog(),
            messenger ?? new StrongReferenceMessenger());
    }

    private static ProductReleaseInfo CreateRelease(string version)
    {
        string fileName = $"SimpleGit11-{version}-win-x64-setup.exe";
        string baseUrl = $"https://github.com/slow-spec85/SimpleGit11/releases/download/v{version}/";
        return new ProductReleaseInfo(
            version,
            new Uri($"https://github.com/slow-spec85/SimpleGit11/releases/tag/v{version}"),
            false,
            new ProductReleaseAsset(
                fileName,
                new Uri(baseUrl + fileName),
                new Uri(baseUrl + fileName + ".sha256"),
                100));
    }

    private sealed class TestProductUpdateService : IProductUpdateService
    {
        public bool Available { get; init; }

        public string InstallerPath { get; init; } = @"C:\Temp\SimpleGit11.Setup.exe";

        public ProductUpdateStage DownloadStage { get; init; }

        public Task<string>? DownloadTask { get; init; }

        public ProductReleaseInfo? AvailabilityRelease { get; private set; }

        public string? AvailabilityCurrentVersion { get; private set; }

        public ProductReleaseInfo? DownloadedRelease { get; private set; }

        public bool IsUpdateAvailable(ProductReleaseInfo release, string currentVersion)
        {
            AvailabilityRelease = release;
            AvailabilityCurrentVersion = currentVersion;
            return Available;
        }

        public Task<string> DownloadInstallerAsync(
            ProductReleaseInfo release,
            Action<ProductUpdateStage>? reportStage,
            CancellationToken cancellationToken)
        {
            DownloadedRelease = release;
            reportStage?.Invoke(DownloadStage);
            return DownloadTask ?? Task.FromResult(InstallerPath);
        }
    }

    private sealed class TestInstallerLauncher : IInstallerLauncher
    {
        public Exception? LaunchException { get; init; }

        public string? InstallerPath { get; private set; }

        public void Launch(string installerPath)
        {
            if (LaunchException is not null)
            {
                throw LaunchException;
            }

            InstallerPath = installerPath;
        }
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public AppLanguage CurrentLanguage => AppLanguage.English;

        public string GetString(string resourceKey) => resourceKey switch
        {
            "AboutUpdateInProgress" => "{0}…",
            "AboutUpdateFailed" => "AboutUpdateFailed ({0})",
            _ => resourceKey
        };

        public void ApplyLanguage()
        {
        }

        public void SetLanguage(AppLanguage language)
        {
        }
    }

    private sealed class TestPluginCatalog(
        IReadOnlyList<PluginMetadata>? plugins = null) : IPluginCatalog
    {
        public IReadOnlyList<PluginMetadata> Plugins { get; } = plugins ?? [];

        public IReadOnlyList<PluginLoadFailure> Failures { get; } = [];
    }
}
