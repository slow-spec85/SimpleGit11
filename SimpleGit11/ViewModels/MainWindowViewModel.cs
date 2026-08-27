using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Extensions;
using SimpleGit11.Messages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Windows.Input;

namespace SimpleGit11.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase,
    IRecipient<AppNotificationMessage>,
    IRecipient<ClearAppNotificationMessage>,
    IRecipient<AppOperationMessage>
{
    private readonly IRecentRepositoriesService _recentRepositoriesService;
    private readonly ILocalizationService _localizationService;
    private readonly IGitService _gitService;
    private readonly IClipboardService _clipboardService;
    private readonly IProductInfoService _productInfoService;
    private readonly Dictionary<object, ActiveOperation> _activeOperations = new(ReferenceEqualityComparer.Instance);
    private object? _notificationSource;
    private string? _pendingChangesNotice;
    private string? _pendingChangesNoticeDetails;
    private long _operationSequence;

    public MainWindowViewModel(
        IRecentRepositoriesService recentRepositoriesService,
        ILocalizationService localizationService,
        IGitService gitService,
        IClipboardService clipboardService,
        IProductInfoService productInfoService,
        IMessenger messenger)
    {
        _recentRepositoriesService = recentRepositoriesService;
        _localizationService = localizationService;
        _gitService = gitService;
        _clipboardService = clipboardService;
        _productInfoService = productInfoService;
        messenger.RegisterAll(this);
        CurrentRepositoryDisplayName = _localizationService.GetString("NoRepositoryOpen");
        SelectedRemoteName = _localizationService.GetString("NoRemote");
        Remotes = [];
        CurrentUserName = "";
        NotificationMessage = "";
        NotificationDetails = "";
        ProgressMessage = "";

        foreach (var repository in _recentRepositoriesService.Load())
        {
            RecentRepositories.Add(repository);
        }

        _ = LoadUserNameAsync(null);
    }

    public string AppName => _productInfoService.ProductName;

    public event EventHandler<NavigationRequestedEventArgs>? NavigationRequested;

    [ObservableProperty]
    public partial bool IsNotificationOpen { get; set; }

    [ObservableProperty]
    public partial string NotificationMessage { get; private set; }

    [ObservableProperty]
    public partial string NotificationDetails { get; private set; }

    [ObservableProperty]
    public partial InfoBarSeverity NotificationSeverity { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotificationActionVisibility))]
    public partial ICommand? NotificationActionCommand { get; private set; }

    [ObservableProperty]
    public partial string? NotificationActionText { get; private set; }

    public Visibility NotificationActionVisibility => NotificationActionCommand is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    [ObservableProperty]
    public partial bool IsOperationRunning { get; private set; }

    [ObservableProperty]
    public partial string ProgressMessage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperationCancelButtonVisibility))]
    public partial ICommand? OperationCancelCommand { get; private set; }

    public Visibility OperationCancelButtonVisibility => OperationCancelCommand is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentBranch))]
    public partial RepositoryInfo? CurrentRepository { get; private set; }

    [ObservableProperty]
    public partial string CurrentRepositoryDisplayName { get; set; }

    public string CurrentBranch => CurrentRepository?.CurrentBranch ?? _localizationService.GetString("NoBranch");

    [ObservableProperty]
    public partial string SelectedRemoteName { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<GitRemote> Remotes { get; private set; }

    public ObservableCollection<RepositoryInfo> RecentRepositories { get; } = [];

    [ObservableProperty]
    public partial RepositoryInfo? SelectedRecentRepository { get; set; }

    [ObservableProperty]
    public partial string CurrentUserName { get; private set; }

    public Visibility GlobalUserNameVisibility => IsCurrentUserFromGlobalConfig
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility LocalOrUnsetUserNameVisibility => IsCurrentUserFromGlobalConfig
        ? Visibility.Collapsed
        : Visibility.Visible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GlobalUserNameVisibility))]
    [NotifyPropertyChangedFor(nameof(LocalOrUnsetUserNameVisibility))]
    private partial bool IsCurrentUserFromGlobalConfig { get; set; }

    partial void OnCurrentRepositoryChanged(RepositoryInfo? value)
    {
        CurrentRepositoryDisplayName = value is null
            ? _localizationService.GetString("NoRepositoryOpen")
            : value.Name;
        _ = LoadUserNameAsync(value);
    }

    partial void OnSelectedRecentRepositoryChanged(RepositoryInfo? value)
    {
        if (value is not null)
        {
            App.GetService<RepositoryViewModel>().OpenRecentRepositoryCommand.TryExecute(value);
            SelectedRecentRepository = null;
        }
    }

    public void SetCurrentRepository(RepositoryInfo repository, IReadOnlyList<RepositoryInfo> recentRepositories)
    {
        if (!string.Equals(CurrentRepository?.Path, repository.Path, StringComparison.OrdinalIgnoreCase))
        {
            SelectedRemoteName = _localizationService.GetString("NoRemote");
            Remotes = [];
        }

        CurrentRepository = repository;
        RecentRepositories.Clear();

        foreach (var recentRepository in recentRepositories)
        {
            RecentRepositories.Add(recentRepository);
        }

    }

    public void CloseCurrentRepository()
    {
        CurrentRepository = null;
        SelectedRemoteName = _localizationService.GetString("NoRemote");
        Remotes = [];
        _ = LoadUserNameAsync(null);
    }

    public void SelectRemote(string? remoteName)
    {
        SelectedRemoteName = remoteName ?? _localizationService.GetString("NoRemote");
    }

    public Task RefreshCurrentUserAsync()
    {
        return LoadUserNameAsync(CurrentRepository);
    }

    public async Task RefreshRemotesAsync()
    {
        RepositoryInfo? repository = CurrentRepository;
        if (repository is null)
        {
            Remotes = [];
            SelectRemote(null);
            return;
        }

        try
        {
            IReadOnlyList<GitRemote> remotes = await _gitService.GetRemotesAsync(repository);
            if (!string.Equals(CurrentRepository?.Path, repository.Path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Remotes = remotes;
            GitRemote? selectedRemote = remotes.FirstOrDefault(remote =>
                    string.Equals(remote.Name, SelectedRemoteName, StringComparison.Ordinal))
                ?? remotes.FirstOrDefault(remote => string.Equals(remote.Name, "origin", StringComparison.Ordinal))
                ?? remotes.FirstOrDefault();
            SelectRemote(selectedRemote?.Name);
        }
        catch
        {
            if (string.Equals(CurrentRepository?.Path, repository.Path, StringComparison.OrdinalIgnoreCase))
            {
                Remotes = [];
                SelectRemote(null);
            }
        }
    }

    public void RequestChangesNavigation(string message, string? details = null)
    {
        _pendingChangesNotice = message;
        _pendingChangesNoticeDetails = details;
        RequestNavigation(AppNavigationTarget.Changes);
    }

    public void RequestNavigation(AppNavigationTarget target, object? parameter = null)
    {
        NavigationRequested?.Invoke(this, new NavigationRequestedEventArgs(target, parameter));
    }

    public bool TryConsumeChangesNotice(out string message, out string? details)
    {
        if (string.IsNullOrWhiteSpace(_pendingChangesNotice))
        {
            message = "";
            details = null;
            return false;
        }

        message = _pendingChangesNotice;
        details = _pendingChangesNoticeDetails;
        _pendingChangesNotice = null;
        _pendingChangesNoticeDetails = null;
        return true;
    }

    private async Task LoadUserNameAsync(RepositoryInfo? repository)
    {
        string name = "";
        bool isFromGlobalConfig = false;
        try
        {
            if (repository is not null)
            {
                name = await _gitService.Configuration.GetUserNameAsync(ConfigScope.Local, repository);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = await _gitService.Configuration.GetUserNameAsync(ConfigScope.Global, null);
                isFromGlobalConfig = !string.IsNullOrWhiteSpace(name);
            }
        }
        catch
        { }

        CurrentUserName = string.IsNullOrWhiteSpace(name) ? _localizationService.GetString("NoUser") : name;
        IsCurrentUserFromGlobalConfig = isFromGlobalConfig;
    }

    public void UpdateCurrentBranch(string branchName)
    {
        if (CurrentRepository is null)
        {
            return;
        }

        if (string.Equals(CurrentRepository.CurrentBranch, branchName, System.StringComparison.Ordinal))
        {
            return;
        }

        CurrentRepository.CurrentBranch = branchName;
        OnPropertyChanged(nameof(CurrentBranch));
    }

    public void UpdateCurrentRepositoryInfo(RepositoryInfo repository)
    {
        if (CurrentRepository is null || CurrentRepository.Path != repository.Path)
        {
            return;
        }

        bool branchChanged = !string.Equals(
            CurrentRepository.CurrentBranch,
            repository.CurrentBranch,
            System.StringComparison.Ordinal);

        CurrentRepository.Name = repository.Name;
        CurrentRepository.CurrentBranch = repository.CurrentBranch;
        CurrentRepository.CommonGitDirectory = repository.CommonGitDirectory;
        CurrentRepository.MainWorktreePath = repository.MainWorktreePath;
        CurrentRepository.IsMainWorktree = repository.IsMainWorktree;
        CurrentRepositoryDisplayName = repository.Name;
        if (branchChanged)
        {
            OnPropertyChanged(nameof(CurrentBranch));
        }
    }

    public void Receive(AppNotificationMessage message)
    {
        _notificationSource = message.Source;
        NotificationSeverity = message.Severity switch
        {
            AppNotificationSeverity.Success => InfoBarSeverity.Success,
            AppNotificationSeverity.Warning => InfoBarSeverity.Warning,
            AppNotificationSeverity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };
        NotificationMessage = message.Message;
        NotificationDetails = message.Details;
        NotificationActionCommand = message.ActionCommand;
        NotificationActionText = message.ActionText;
        IsNotificationOpen = true;
    }

    public void Receive(ClearAppNotificationMessage message)
    {
        if (!ReferenceEquals(_notificationSource, message.Source))
        {
            return;
        }

        _notificationSource = null;
        IsNotificationOpen = false;
        NotificationMessage = "";
        NotificationDetails = "";
        NotificationActionCommand = null;
        NotificationActionText = null;
    }

    public void Receive(AppOperationMessage message)
    {
        if (message.IsRunning)
        {
            _activeOperations[message.Source] = new ActiveOperation(
                message.Message,
                message.CancelCommand,
                ++_operationSequence);
        }
        else
        {
            _activeOperations.Remove(message.Source);
        }

        ActiveOperation? operation = _activeOperations.Values.MaxBy(item => item.Sequence);
        IsOperationRunning = operation is not null;
        ProgressMessage = operation?.Message ?? "";
        OperationCancelCommand = operation?.CancelCommand;
    }

    [RelayCommand]
    private void OnCopyNotificationText(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            _clipboardService.SetText(text);
        }
    }

    [RelayCommand]
    private void OnRemoveRecentRepository(RepositoryInfo? repository)
    {
        if (repository is null)
        {
            return;
        }

        IReadOnlyList<RepositoryInfo> repositories = _recentRepositoriesService.Remove(repository);
        RecentRepositories.Clear();
        foreach (RepositoryInfo recentRepository in repositories)
        {
            RecentRepositories.Add(recentRepository);
        }
    }

    private sealed record ActiveOperation(
        string Message,
        ICommand? CancelCommand,
        long Sequence);
}
