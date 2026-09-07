using SimpleGit11.Extensibility.Plugins;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Tests.TestInfrastructure;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class AboutDialogViewModelTests
{
    [TestMethod]
    public async Task LoadAsync_NewerStableRelease_ExposesUpdateCommand()
    {
        ProductReleaseInfo release = CreateRelease("1.2.3");
        TestProductInfoService productInfoService = new() { LatestRelease = release };
        TestProductUpdateService updateService = new() { Available = true };
        using AboutDialogViewModel viewModel = CreateViewModel(
            productInfoService,
            updateService,
            new TestInstallerLauncher());

        await viewModel.LoadAsync();

        Assert.IsTrue(viewModel.HasLatestRelease);
        Assert.AreEqual("1.2.3", viewModel.LatestReleaseVersion);
        Assert.IsNotNull(viewModel.LatestReleaseUri);
        Assert.IsTrue(viewModel.CanInstallUpdate);
        Assert.IsFalse(viewModel.HasReleaseStatus);
        Assert.AreEqual(1, productInfoService.ReleaseRequests);
        Assert.AreEqual(release, updateService.AvailabilityRelease);
        Assert.AreEqual("1.0.0", updateService.AvailabilityCurrentVersion);
        Assert.IsFalse(viewModel.HasSshPlugin);
        Assert.AreEqual(string.Empty, viewModel.SshPluginVersion);
    }

    [TestMethod]
    public async Task LoadAsync_CurrentRelease_HidesUpdateCommand()
    {
        TestProductUpdateService updateService = new() { Available = false };
        using AboutDialogViewModel viewModel = CreateViewModel(
            new TestProductInfoService { LatestRelease = CreateRelease("1.0.0") },
            updateService,
            new TestInstallerLauncher());

        await viewModel.LoadAsync();

        Assert.IsTrue(viewModel.HasLatestRelease);
        Assert.IsFalse(viewModel.CanInstallUpdate);
    }

    [TestMethod]
    public async Task InstallUpdateCommand_DownloadsLaunchesAndRequestsApplicationExit()
    {
        ProductReleaseInfo release = CreateRelease("1.2.3");
        TestProductUpdateService updateService = new()
        {
            Available = true,
            InstallerPath = @"C:\Temp\SimpleGit11-1.2.3-win-x64.msi"
        };
        TestInstallerLauncher launcher = new();
        using AboutDialogViewModel viewModel = CreateViewModel(
            new TestProductInfoService { LatestRelease = release },
            updateService,
            launcher);
        bool installerLaunched = false;
        viewModel.InstallerLaunched += (_, _) => installerLaunched = true;
        await viewModel.LoadAsync();

        await viewModel.InstallUpdateCommand.ExecuteAsync(null);

        Assert.AreEqual(release, updateService.DownloadedRelease);
        Assert.AreEqual(updateService.InstallerPath, launcher.InstallerPath);
        Assert.IsTrue(installerLaunched);
        Assert.IsFalse(viewModel.IsDownloadingUpdate);
    }

    [TestMethod]
    public async Task InstallUpdateCommand_DownloadFails_ShowsLocalizedErrorAndDoesNotLaunch()
    {
        TestProductUpdateService updateService = new()
        {
            Available = true,
            DownloadException = new InvalidDataException("Checksum mismatch")
        };
        TestInstallerLauncher launcher = new();
        using AboutDialogViewModel viewModel = CreateViewModel(
            new TestProductInfoService { LatestRelease = CreateRelease("1.2.3") },
            updateService,
            launcher);
        await viewModel.LoadAsync();

        await viewModel.InstallUpdateCommand.ExecuteAsync(null);

        Assert.IsNull(launcher.InstallerPath);
        Assert.IsTrue(viewModel.HasUpdateStatus);
        Assert.AreEqual("AboutUpdateFailed", viewModel.UpdateStatusMessage);
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
        IPluginCatalog? plugins = null)
    {
        return new AboutDialogViewModel(
            productInfoService,
            updateService,
            launcher,
            new TestLocalizationService(),
            plugins ?? new TestPluginCatalog());
    }

    private static ProductReleaseInfo CreateRelease(string version)
    {
        string fileName = $"SimpleGit11-{version}-win-x64.msi";
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

        public string InstallerPath { get; init; } = @"C:\Temp\SimpleGit11.msi";

        public Exception? DownloadException { get; init; }

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
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            DownloadedRelease = release;
            return DownloadException is null
                ? Task.FromResult(InstallerPath)
                : Task.FromException<string>(DownloadException);
        }
    }

    private sealed class TestInstallerLauncher : IInstallerLauncher
    {
        public string? InstallerPath { get; private set; }

        public void Launch(string installerPath)
        {
            InstallerPath = installerPath;
        }
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public AppLanguage CurrentLanguage => AppLanguage.English;

        public string GetString(string resourceKey) => resourceKey;

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
