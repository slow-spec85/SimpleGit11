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
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localizationService;
    private CancellationTokenSource? _releaseRequestCancellation;
    private bool _canPersistPrereleasePreference;
    private bool _isDialogOpen;
    private long _releaseRequestSequence;

    public AboutDialogViewModel(
        IProductInfoService productInfoService,
        ISettingsService settingsService,
        ILocalizationService localizationService,
        IPluginCatalog pluginCatalog)
    {
        _productInfoService = productInfoService;
        _settingsService = settingsService;
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
        IncludePrereleaseVersions = settingsService.Current.IncludePrereleaseVersions;
        _canPersistPrereleasePreference = true;
    }

    public string ProductName { get; }

    public string CurrentVersion { get; }

    public Uri RepositoryUri { get; }

    public string RepositoryDisplayUri { get; }

    public bool HasSshPlugin { get; }

    public string SshPluginVersion { get; }

    [ObservableProperty]
    public partial bool IncludePrereleaseVersions { get; set; }

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

    public Task LoadAsync()
    {
        _isDialogOpen = true;
        return LoadLatestReleaseAsync();
    }

    public void Dispose()
    {
        _isDialogOpen = false;
        CancellationTokenSource? cancellation = Interlocked.Exchange(
            ref _releaseRequestCancellation,
            null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    partial void OnIncludePrereleaseVersionsChanged(bool value)
    {
        if (!_canPersistPrereleasePreference)
        {
            return;
        }

        _settingsService.SetIncludePrereleaseVersions(value);
        if (_isDialogOpen)
        {
            _ = LoadLatestReleaseAsync();
        }
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
        return !IsLoadingLatestRelease;
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
                IncludePrereleaseVersions,
                cancellationToken);
            if (requestSequence != _releaseRequestSequence)
            {
                return;
            }

            if (release is null)
            {
                ReleaseStatusMessage = _localizationService.GetString(
                    IncludePrereleaseVersions
                        ? "AboutNoPublishedRelease"
                        : "AboutNoStableRelease");
                HasReleaseStatus = true;
                return;
            }

            LatestReleaseVersion = release.Version;
            LatestReleaseUri = release.Uri;
            HasLatestRelease = true;
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
        LatestReleaseVersion = "";
        LatestReleaseUri = null;
        HasReleaseStatus = false;
        HasReleaseError = false;
        ReleaseStatusMessage = "";
    }
}
