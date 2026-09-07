using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimpleGit11.Extensibility.Plugins;
using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.ViewModels;

public sealed partial class AboutDialogViewModel : ViewModelBase, IDisposable
{
    private readonly IProductInfoService _productInfoService;
    private readonly IProductUpdateService _productUpdateService;
    private readonly IInstallerLauncher _installerLauncher;
    private readonly ILocalizationService _localizationService;
    private CancellationTokenSource? _releaseRequestCancellation;
    private CancellationTokenSource? _updateCancellation;
    private ProductReleaseInfo? _latestRelease;
    private long _releaseRequestSequence;

    public AboutDialogViewModel(
        IProductInfoService productInfoService,
        IProductUpdateService productUpdateService,
        IInstallerLauncher installerLauncher,
        ILocalizationService localizationService,
        IPluginCatalog pluginCatalog)
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
        UpdateStatusMessage = "";
    }

    public event EventHandler? InstallerLaunched;

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
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    public partial bool CanInstallUpdate { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshLatestReleaseCommand))]
    public partial bool IsDownloadingUpdate { get; private set; }

    [ObservableProperty]
    public partial bool HasUpdateStatus { get; private set; }

    [ObservableProperty]
    public partial string UpdateStatusMessage { get; private set; }

    public Task LoadAsync()
    {
        return LoadLatestReleaseAsync();
    }

    public void Dispose()
    {
        CancelAndDispose(ref _releaseRequestCancellation);
        CancelAndDispose(ref _updateCancellation);
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

    [RelayCommand(
        CanExecute = nameof(CanStartUpdate),
        FlowExceptionsToTaskScheduler = true)]
    private async Task OnInstallUpdateAsync()
    {
        ProductReleaseInfo? release = _latestRelease;
        if (release is null || !CanStartUpdate())
        {
            return;
        }

        CancellationTokenSource cancellation = new();
        CancellationTokenSource? previousCancellation = Interlocked.Exchange(
            ref _updateCancellation,
            cancellation);
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();
        CancellationToken cancellationToken = cancellation.Token;
        HasUpdateStatus = true;
        UpdateStatusMessage = _localizationService.GetString("AboutDownloadingUpdate");
        IsDownloadingUpdate = true;

        try
        {
            string installerPath = await _productUpdateService.DownloadInstallerAsync(
                release,
                null,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _installerLauncher.Launch(installerPath);
            InstallerLaunched?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            UpdateStatusMessage = _localizationService.GetString("AboutUpdateFailed");
            HasUpdateStatus = true;
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    private bool CanStartUpdate()
    {
        return CanInstallUpdate && !IsDownloadingUpdate;
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
        HasUpdateStatus = false;
        UpdateStatusMessage = "";
    }

    private static void CancelAndDispose(ref CancellationTokenSource? source)
    {
        CancellationTokenSource? cancellation = Interlocked.Exchange(ref source, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }
}
