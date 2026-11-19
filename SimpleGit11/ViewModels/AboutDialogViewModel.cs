using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Extensibility.Plugins;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.ViewModels;

public sealed partial class AboutDialogViewModel : AppNotificationViewModelBase, IDisposable
{
    private readonly IProductInfoService _productInfoService;
    private readonly IProductUpdateService _productUpdateService;
    private readonly IInstallerLauncher _installerLauncher;
    private readonly ILocalizationService _localizationService;
    private CancellationTokenSource? _releaseRequestCancellation;
    private ProductReleaseInfo? _latestRelease;
    private long _releaseRequestSequence;

    public AboutDialogViewModel(
        IProductInfoService productInfoService,
        IProductUpdateService productUpdateService,
        IInstallerLauncher installerLauncher,
        ILocalizationService localizationService,
        IPluginCatalog pluginCatalog,
        IMessenger messenger)
        : base(messenger)
    {
        _productInfoService = productInfoService;
        _productUpdateService = productUpdateService;
        _installerLauncher = installerLauncher;
        _localizationService = localizationService;
        ProductName = productInfoService.ProductName;
        CurrentVersion = productInfoService.CurrentVersion;
        RepositoryUri = productInfoService.RepositoryUri;
        RepositoryDisplayUri = productInfoService.RepositoryUri.AbsoluteUri.TrimEnd('/');
        PluginMetadata? sshPlugin = pluginCatalog.Plugins.FirstOrDefault(static plugin =>
            string.Equals(plugin.Id, "simplegit11.ssh", StringComparison.OrdinalIgnoreCase));
        HasSshPlugin = sshPlugin is not null;
        SshPluginVersion = sshPlugin?.Version ?? "";
        LatestReleaseVersion = "";
        ReleaseStatusMessage = "";
    }

    public string ProductName { get; }

    public string CurrentVersion { get; }

    public Uri RepositoryUri { get; }

    public string RepositoryDisplayUri { get; }

    public bool HasSshPlugin { get; }

    public string SshPluginVersion { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshLatestReleaseCommand))]
    public partial bool IsLoadingLatestRelease { get; private set; }

    [ObservableProperty]
    public partial bool HasLatestRelease { get; private set; }

    [ObservableProperty]
    public partial string LatestReleaseVersion { get; private set; }

    [ObservableProperty]
    public partial Uri? LatestReleaseUri { get; private set; }

    [ObservableProperty]
    public partial bool HasReleaseStatus { get; private set; }

    [ObservableProperty]
    public partial bool HasReleaseError { get; private set; }

    [ObservableProperty]
    public partial string ReleaseStatusMessage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallUpdateButtonText))]
    public partial bool CanInstallUpdate { get; private set; }

    public string InstallUpdateButtonText =>
        CanInstallUpdate ? _localizationService.GetString("AboutInstallUpdateButtonText") : "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshLatestReleaseCommand))]
    public partial bool IsDownloadingUpdate { get; private set; }

    public Task LoadAsync()
    {
        return LoadLatestReleaseAsync();
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation = Interlocked.Exchange(ref _releaseRequestCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    [RelayCommand(
        CanExecute = nameof(CanRefreshLatestRelease),
        FlowExceptionsToTaskScheduler = true)]
    private Task OnRefreshLatestReleaseAsync()
    {
        return LoadLatestReleaseAsync();
    }

    private bool CanRefreshLatestRelease()
    {
        return !IsLoadingLatestRelease && !IsDownloadingUpdate;
    }

    public async Task<bool> InstallUpdateAsync()
    {
        ProductReleaseInfo? release = _latestRelease;
        if (release is null || !CanInstallUpdate || IsDownloadingUpdate)
        {
            return false;
        }

        SynchronizationContext? uiContext = SynchronizationContext.Current;
        ProductUpdateStage stage = ProductUpdateStage.ValidateRelease;
        IsDownloadingUpdate = true;

        try
        {
            PublishOperationState(true, GetUpdateStageMessage(stage, "AboutUpdateInProgress"));
            string installerPath = await _productUpdateService.DownloadInstallerAsync(
                release,
                ReportDownloadStage,
                CancellationToken.None);
            stage = ProductUpdateStage.LaunchInstaller;
            PublishOperationState(true, GetUpdateStageMessage(stage, "AboutUpdateInProgress"));
            _installerLauncher.Launch(installerPath);
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            ShowNotification(
                AppNotificationSeverity.Error,
                GetUpdateStageMessage(stage, "AboutUpdateFailed"));
            return false;
        }
        finally
        {
            IsDownloadingUpdate = false;
            PublishOperationState(false);
        }

        void ReportDownloadStage(ProductUpdateStage reportedStage)
        {
            stage = reportedStage;
            if (uiContext is null || SynchronizationContext.Current == uiContext)
            {
                DisplayDownloadStage(reportedStage);
            }
            else
            {
                uiContext.Post(_ => DisplayDownloadStage(reportedStage), null);
            }
        }

        void DisplayDownloadStage(ProductUpdateStage reportedStage)
        {
            if (IsDownloadingUpdate && stage != ProductUpdateStage.LaunchInstaller)
            {
                PublishOperationState(true, GetUpdateStageMessage(reportedStage, "AboutUpdateInProgress"));
            }
        }
    }

    private string GetUpdateStageMessage(ProductUpdateStage stage, string formatKey)
    {
        string stageKey = stage switch
        {
            ProductUpdateStage.ValidateRelease => "AboutUpdateValidateRelease",
            ProductUpdateStage.DownloadChecksum => "AboutUpdateDownloadChecksum",
            ProductUpdateStage.DownloadChecksumViaApi => "AboutUpdateDownloadChecksumViaApi",
            ProductUpdateStage.ValidateChecksum => "AboutUpdateValidateChecksum",
            ProductUpdateStage.PrepareDownload => "AboutUpdatePrepareDownload",
            ProductUpdateStage.DownloadInstaller => "AboutUpdateDownloadInstaller",
            ProductUpdateStage.DownloadInstallerViaApi => "AboutUpdateDownloadInstallerViaApi",
            ProductUpdateStage.VerifyInstaller => "AboutUpdateVerifyInstaller",
            ProductUpdateStage.SaveInstaller => "AboutUpdateSaveInstaller",
            ProductUpdateStage.LaunchInstaller => "AboutUpdateLaunchInstaller",
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };
        return string.Format(
            _localizationService.GetString(formatKey),
            _localizationService.GetString(stageKey));
    }

    private async Task LoadLatestReleaseAsync()
    {
        CancellationTokenSource cancellation = new();
        CancellationTokenSource? previousCancellation = Interlocked.Exchange(
            ref _releaseRequestCancellation,
            cancellation);
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();

        long requestSequence = Interlocked.Increment(ref _releaseRequestSequence);
        CancellationToken cancellationToken = cancellation.Token;
        ClearReleaseState();
        IsLoadingLatestRelease = true;

        try
        {
            ProductReleaseInfo? release = await _productInfoService.GetLatestReleaseAsync(
                cancellationToken);
            if (requestSequence != _releaseRequestSequence)
            {
                return;
            }

            if (release is null)
            {
                ReleaseStatusMessage = _localizationService.GetString("AboutNoStableRelease");
                HasReleaseStatus = true;
                return;
            }

            LatestReleaseVersion = release.Version;
            LatestReleaseUri = release.Uri;
            _latestRelease = release;
            HasLatestRelease = true;
            CanInstallUpdate = _productUpdateService.IsUpdateAvailable(release, CurrentVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            if (requestSequence == _releaseRequestSequence)
            {
                ReleaseStatusMessage = _localizationService.GetString("AboutReleaseCheckFailed");
                HasReleaseError = true;
                HasReleaseStatus = true;
            }
        }
        finally
        {
            if (requestSequence == _releaseRequestSequence)
            {
                IsLoadingLatestRelease = false;
            }
        }
    }

    private void ClearReleaseState()
    {
        HasLatestRelease = false;
        CanInstallUpdate = false;
        _latestRelease = null;
        LatestReleaseVersion = "";
        LatestReleaseUri = null;
        HasReleaseStatus = false;
        HasReleaseError = false;
        ReleaseStatusMessage = "";
    }
}
