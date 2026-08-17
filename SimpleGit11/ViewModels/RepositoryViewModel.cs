using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json.Linq;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SimpleGit11.ViewModels;

public sealed partial class RepositoryViewModel : AppNotificationViewModelBase
{
    private readonly IStoragePickerService _storagePickerService;
    private readonly IRecentRepositoriesService _recentRepositoriesService;
    private readonly IGitService _gitService;
    private readonly ILocalizationService _localizationService;
    private readonly IClipboardService _clipboardService;
    private readonly IFileExplorerService _fileExplorerService;
    private readonly IDialogService _dialogService;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly IAsyncCommandExecutor _asyncCommandExecutor;
    private readonly List<FoundRepositoryViewItem> _foundRepositoryItems = [];
    private IReadOnlyList<GitRemote> _remotes = [];

    public RepositoryViewModel(
        IStoragePickerService storagePickerService,
        IRecentRepositoriesService recentRepositoriesService,
        IGitService gitService,
        ILocalizationService localizationService,
        IClipboardService clipboardService,
        IFileExplorerService fileExplorerService,
        IDialogService dialogService,
        MainWindowViewModel mainWindowViewModel,
        IMessenger messenger,
        IAsyncCommandExecutor asyncCommandExecutor)
        : base(messenger)
    {
        _storagePickerService = storagePickerService;
        _recentRepositoriesService = recentRepositoriesService;
        _gitService = gitService;
        _localizationService = localizationService;
        _clipboardService = clipboardService;
        _fileExplorerService = fileExplorerService;
        _dialogService = dialogService;
        _mainWindowViewModel = mainWindowViewModel;
        _asyncCommandExecutor = asyncCommandExecutor
            ?? throw new ArgumentNullException(nameof(asyncCommandExecutor));
        RepositoryName = _localizationService.GetString("NoRepositoryOpen");
        RepositoryPath = "";
        CurrentBranch = _localizationService.GetString("NoBranch");
        ProgressMessage = "";
        CurrentUser = "";
        CurrentEmail = "";
        RepositoryState = "";
        LastCommitSummary = "";
        LastCommitDetails = "";
        TrackingSummary = "";
        TrackingUrl = "";
        TrackingRemoteName = "";
        PushSummary = "";
        PushUrl = "";
        CloneRepositoryUrl = "";
        FoundRepositoryFilterText = "";
        RepositorySearchStartPath = _gitService.RepositorySearch.LoadStartPath();
        ResetRepositoryDetails();

        LoadPersistedFoundRepositories();
    }

    private bool CanRunWhenIdle()
    {
        return !IsGitOperationRunning;
    }

    private bool CanRunWithOpenRepository()
    {
        return IsRepositoryOpen && !IsGitOperationRunning;
    }

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnOpenRepositoryAsync() => _asyncCommandExecutor.ExecuteAsync(OpenRepositoryAsync);

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnCreateRepositoryAsync() => _asyncCommandExecutor.ExecuteAsync(CreateRepositoryAsync);

    [RelayCommand(CanExecute = nameof(CanCloneRepository), FlowExceptionsToTaskScheduler = true)]
    private Task OnCloneRepositoryAsync() => _asyncCommandExecutor.ExecuteAsync(CloneRepositoryAsync);

    [RelayCommand(CanExecute = nameof(CanRunGitOperation), FlowExceptionsToTaskScheduler = true)]
    private Task OnAddRemoteAsync() => _asyncCommandExecutor.ExecuteAsync(AddRemoteAsync);

    [RelayCommand(CanExecute = nameof(CanRunGitOperation), FlowExceptionsToTaskScheduler = true)]
    private Task OnRenameRemoteAsync(RemoteViewItem? item) =>
        _asyncCommandExecutor.ExecuteAsync(() => RenameRemoteAsync(item));

    [RelayCommand(CanExecute = nameof(CanRunGitOperation), FlowExceptionsToTaskScheduler = true)]
    private Task OnEditRemoteUrlAsync(RemoteViewItem? item) => 
        _asyncCommandExecutor.ExecuteAsync(() => EditRemoteUrlAsync(item));

    [RelayCommand(CanExecute = nameof(CanRemoveTrackingRemote), FlowExceptionsToTaskScheduler = true)]
    private Task OnRemoveRemoteAsync(RemoteViewItem? item) => 
        _asyncCommandExecutor.ExecuteAsync(() => RemoveRemoteAsync(item));

    [RelayCommand(CanExecute = nameof(CanRunWithOpenRepository), FlowExceptionsToTaskScheduler = true)]
    private Task OnRefreshRepositoryAsync() =>
        _asyncCommandExecutor.ExecuteAsync(RefreshCurrentRepositoryAsync);

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnBrowseRepositorySearchStartPathAsync() =>
        _asyncCommandExecutor.ExecuteAsync(BrowseRepositorySearchStartPathAsync);

    [RelayCommand(CanExecute = nameof(CanSearchRepositories), FlowExceptionsToTaskScheduler = true)]
    private Task OnSearchRepositoriesAsync() => _asyncCommandExecutor.ExecuteAsync(SearchRepositoriesAsync);

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnOpenFoundRepositoryAsync(RepositoryInfo? repository) =>
        _asyncCommandExecutor.ExecuteAsync(() => OpenFoundRepositoryAsync(repository));

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnOpenRecentRepositoryAsync(RepositoryInfo? repository) =>
        _asyncCommandExecutor.ExecuteAsync(() => OpenRecentRepositoryAsync(repository));

    [RelayCommand(CanExecute = nameof(CanRunWithOpenRepository), FlowExceptionsToTaskScheduler = true)]
    private Task OnCreateWorktreeAsync() => _asyncCommandExecutor.ExecuteAsync(CreateWorktreeAsync);

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnOpenWorktreeAsync(WorktreeViewItem? item) =>
        _asyncCommandExecutor.ExecuteAsync(() => OpenWorktreeAsync(item));

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnMoveWorktreeAsync(WorktreeViewItem? item) =>
        _asyncCommandExecutor.ExecuteAsync(() => MoveWorktreeAsync(item));

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnRemoveWorktreeAsync(WorktreeViewItem? item) =>
        _asyncCommandExecutor.ExecuteAsync(() => RemoveWorktreeAsync(item));

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnToggleWorktreeLockAsync(WorktreeViewItem? item) =>
        _asyncCommandExecutor.ExecuteAsync(() => ToggleWorktreeLockAsync(item));

    [RelayCommand(CanExecute = nameof(CanRunWithOpenRepository), FlowExceptionsToTaskScheduler = true)]
    private Task OnPruneWorktreesAsync() => _asyncCommandExecutor.ExecuteAsync(PruneWorktreesAsync);

    [RelayCommand(CanExecute = nameof(CanRunWithOpenRepository), FlowExceptionsToTaskScheduler = true)]
    private Task OnRepairWorktreesAsync() => _asyncCommandExecutor.ExecuteAsync(RepairWorktreesAsync);

    [RelayCommand(CanExecute = nameof(CanRunWithOpenRepository), FlowExceptionsToTaskScheduler = true)]
    private Task OnArchiveAsync() => _asyncCommandExecutor.ExecuteAsync(ArchiveAsync);

    [RelayCommand(CanExecute = nameof(CanCloseRepository))]
    private void OnCloseRepository()
    {
        CloseRepository();
    }

    private bool CanCloseRepository()
    {
        return IsRepositoryOpen && !IsGitOperationRunning;
    }

    [RelayCommand(CanExecute = nameof(CanCopyRepositoryPath))]
    private void OnCopyRepositoryPath()
    {
        _clipboardService.SetText(RepositoryPath);
    }

    private bool CanCopyRepositoryPath()
    {
        return !string.IsNullOrWhiteSpace(RepositoryPath);
    }

    [RelayCommand]
    private void OnCopyText(string? text)
    {
        if (text is not null)
        {
            _clipboardService.SetText(text);
        }
    }

    public ObservableCollection<RepositoryInfo> FoundRepositories { get; } = [];

    public ObservableCollection<FoundRepositoryViewItem> FilteredFoundRepositories { get; } = [];

    public ObservableCollection<WorktreeViewItem> Worktrees { get; } = [];

    public ObservableCollection<RemoteViewItem> RemoteViewItems { get; } = [];


    [ObservableProperty]
    public partial string RepositoryName { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepositoryPathDisplayText))]
    [NotifyCanExecuteChangedFor(nameof(CopyRepositoryPathCommand))]
    public partial string RepositoryPath { get; private set; }

    public string RepositoryPathDisplayText => string.IsNullOrWhiteSpace(RepositoryPath)
        ? _localizationService.GetString("RepositoryPathUnavailable")
        : RepositoryPath;

    [ObservableProperty]
    public partial string CurrentBranch { get; private set; }

    [ObservableProperty]
    public partial string ProgressMessage { get; private set; }

    [ObservableProperty]
    public partial string CurrentUser { get; set; }

    [ObservableProperty]
    public partial string CurrentEmail { get; set; }

    [ObservableProperty]
    public partial string RepositoryState { get; private set; }

    [ObservableProperty]
    public partial string LastCommitSummary { get; private set; }

    [ObservableProperty]
    public partial string LastCommitDetails { get; private set; }

    [ObservableProperty]
    public partial string TrackingSummary { get; private set; }

    [ObservableProperty]
    public partial string TrackingUrl { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemoveTrackingRemote))]
    [NotifyCanExecuteChangedFor(nameof(RemoveRemoteCommand))]
    public partial string TrackingRemoteName { get; private set; }

    [ObservableProperty]
    public partial string PushSummary { get; private set; }

    [ObservableProperty]
    public partial string PushUrl { get; private set; }

    [ObservableProperty]
    public partial bool HasPushTarget { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCloneRepository))]
    [NotifyCanExecuteChangedFor(nameof(CloneRepositoryCommand))]
    public partial string CloneRepositoryUrl { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSearchRepositories))]
    [NotifyCanExecuteChangedFor(nameof(SearchRepositoriesCommand))]
    public partial string RepositorySearchStartPath { get; set; }

    [ObservableProperty]
    public partial string FoundRepositoryFilterText { get; set; }

    [ObservableProperty]
    public partial WorktreeViewItem? CurrentWorktreeItem { get; private set; }

    [ObservableProperty]
    public partial RemoteViewItem? CurrentRemoteItem { get; private set; }

    [ObservableProperty]
    public partial int StagedCount { get; private set; }

    [ObservableProperty]
    public partial int UnstagedCount { get; private set; }

    [ObservableProperty]
    public partial int ConflictCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperationProgressVisibility))]
    [NotifyPropertyChangedFor(nameof(CanCloneRepository))]
    [NotifyPropertyChangedFor(nameof(CanSearchRepositories))]
    [NotifyPropertyChangedFor(nameof(HasNoFoundRepositories))]
    [NotifyPropertyChangedFor(nameof(HasNoFilteredFoundRepositories))]
    public partial bool IsGitOperationRunning { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFoundRepositoryCommand))]
    public partial RepositoryInfo? SelectedFoundRepository { get; set; }

    partial void OnProgressMessageChanged(string value)
    {
        PublishRepositoryOperationState();
    }

    partial void OnRepositorySearchStartPathChanged(string value)
    {
        _gitService.RepositorySearch.SaveStartPath(value);
    }

    partial void OnFoundRepositoryFilterTextChanged(string value)
    {
        ApplyFoundRepositoryFilter();
    }

    partial void OnIsGitOperationRunningChanged(bool value)
    {
        UpdateCommandStates();
        PublishRepositoryOperationState();
    }

    public bool IsRepositoryOpen => _mainWindowViewModel.CurrentRepository is not null;

    public bool CanRunGitOperation => IsRepositoryOpen && !IsGitOperationRunning;

    public bool CanRemoveTrackingRemote => IsRepositoryOpen
        && !IsGitOperationRunning
        && !string.IsNullOrWhiteSpace(TrackingRemoteName);

    public bool CanCloneRepository => !IsGitOperationRunning && !string.IsNullOrWhiteSpace(CloneRepositoryUrl);

    public bool CanSearchRepositories => !IsGitOperationRunning && !string.IsNullOrWhiteSpace(RepositorySearchStartPath);

    public bool HasNoFoundRepositories => !IsGitOperationRunning && FoundRepositories.Count == 0;

    public bool HasNoFilteredFoundRepositories => !IsGitOperationRunning
        && FoundRepositories.Count > 0
        && FilteredFoundRepositories.Count == 0;

    public bool HasNoWorktrees => IsRepositoryOpen && Worktrees.Count == 0;
    public bool HasNoRemotes => IsRepositoryOpen && RemoteViewItems.Count == 0;

    public Visibility OperationProgressVisibility => IsGitOperationRunning ? Visibility.Visible : Visibility.Collapsed;

    private void PublishRepositoryOperationState()
    {
        PublishOperationState(IsGitOperationRunning, ProgressMessage);
    }

    private async Task OpenRepositoryAsync()
    {
        ClearResultMessages();

        string? selectedPath = await _storagePickerService.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        RepositoryInfo? repository = _gitService.RepositoryDiscovery.TryOpenRepository(selectedPath);
        if (repository is null)
        {
            ShowError(_localizationService.GetString("SelectedFolderNotGitRepository"));
            return;
        }

        await OpenRepositoryAsync(repository);
    }

    private async Task CreateRepositoryAsync()
    {
        ClearResultMessages();

        string? selectedPath = await _storagePickerService.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        await RunGitOperationAsync(_localizationService.GetString("CreatingRepositoryProgress"), async () =>
        {
            try
            {
                RepositoryInfo repository = await _gitService.RepositoryOperations.CreateAsync(selectedPath);
                await OpenRepositoryAsync(repository);
                ShowSuccess(string.Format(_localizationService.GetString("RepositoryCreated"), repository.Name));
            }
            catch (FileNotFoundException)
            {
                ShowError(_localizationService.GetString("GitExecutableNotFound"));
            }
            catch (DirectoryNotFoundException)
            {
                ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
            }
            catch (GitCommandException exception)
            {
                ShowError(_localizationService.GetString("GitInitCommandFailed"), exception.Message);
            }
        });
    }

    private async Task ArchiveAsync()
    {
        ClearResultMessages();

        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        if (repository is null)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeArchive"));
            return;
        }

        GitArchiveDialogResult? dialogResult = await _dialogService.ShowArchiveDialogAsync(repository);
        if (dialogResult is null)
        {
            return;
        }

        string archiveName = CreateArchiveName(repository, dialogResult.StartPoint);
        string? outputPath;
        try
        {
            outputPath = await _storagePickerService.PickArchiveFileAsync(
                archiveName,
                dialogResult.Format);
        }
        catch (Exception exception)
        {
            ShowError(_localizationService.GetString("ArchiveFileSelectionFailed"), exception.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        GitArchiveRequest request = new(
            dialogResult.ResolvedCommitHash,
            outputPath,
            dialogResult.Format,
            dialogResult.IncludeRootDirectory ? archiveName : "");

        await RunGitOperationAsync(_localizationService.GetString("CreatingArchiveProgress"), async () =>
        {
            try
            {
                await _gitService.Archive.CreateAsync(repository, request, System.Threading.CancellationToken.None);
                ShowSuccess(_localizationService.GetString("ArchiveCreated"), outputPath);
            }
            catch (FileNotFoundException)
            {
                ShowError(_localizationService.GetString("GitExecutableNotFound"));
            }
            catch (DirectoryNotFoundException exception)
            {
                ShowError(_localizationService.GetString("ArchiveFileWriteFailed"), exception.Message);
            }
            catch (GitCommandException exception)
            {
                ShowError(_localizationService.GetString("GitArchiveCommandFailed"), exception.Message);
            }
            catch (IOException exception)
            {
                ShowError(_localizationService.GetString("ArchiveFileWriteFailed"), exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                ShowError(_localizationService.GetString("ArchiveFileWriteFailed"), exception.Message);
            }
            catch (ArgumentException exception)
            {
                ShowError(_localizationService.GetString("ArchiveFileWriteFailed"), exception.Message);
            }
        });
    }

    private async Task CloneRepositoryAsync()
    {
        ClearResultMessages();

        string remoteUrl = CloneRepositoryUrl.Trim();
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return;
        }

        string? selectedPath = await _storagePickerService.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        await RunGitOperationAsync(_localizationService.GetString("CloningRepositoryProgress"), async () =>
        {
            try
            {
                RepositoryInfo repository = await _gitService.RepositoryOperations.CloneAsync(selectedPath, remoteUrl);
                await OpenRepositoryAsync(repository);
                CloneRepositoryUrl = "";
                ShowSuccess(string.Format(_localizationService.GetString("RepositoryCloned"), repository.Name));
            }
            catch (FileNotFoundException)
            {
                ShowError(_localizationService.GetString("GitExecutableNotFound"));
            }
            catch (DirectoryNotFoundException)
            {
                ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
            }
            catch (GitCommandException exception)
            {
                ShowError(_localizationService.GetString("GitCloneCommandFailed"), exception.Message);
            }
        });
    }

    private async Task AddRemoteAsync()
    {
        ClearResultMessages();

        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        if (repository is null)
        {
            return;
        }

        string? remoteName = await GetRemoteNameForNewRemoteAsync(_remotes);
        if (string.IsNullOrWhiteSpace(remoteName))
        {
            return;
        }

        string? remoteUrl = await _dialogService.ShowTextInputAsync(new TextInputDialogRequest(
            string.Format(_localizationService.GetString("AddRemoteUrlDialogTitle"), remoteName),
            _localizationService.GetString("AddRemoteUrlDialogTextBoxHeader"),
            "",
            _localizationService.GetString("AddRemoteUrlDialogPrimaryButton"),
            _localizationService.GetString("TextInputDialogCancelButton"),
            _localizationService.GetString("RemoteUrlPlaceholder")));

        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return;
        }

        await RunGitOperationAsync(string.Format(_localizationService.GetString("AddRemoteProgress"), remoteName), async () =>
        {
            try
            {
                GitRemoteOperationResult result = await _gitService.Remotes.AddRemoteAsync(repository, remoteName, remoteUrl);
                await LoadRemoteDetailsAsync(repository);
                string successMessage = string.Format(_localizationService.GetString("RemoteAdded"), remoteName);
                ShowSuccess(successMessage, result.Output);
            }
            catch (FileNotFoundException)
            {
                ShowError(_localizationService.GetString("GitExecutableNotFound"));
            }
            catch (DirectoryNotFoundException)
            {
                ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
            }
            catch (GitCommandException exception)
            {
                ShowError(_localizationService.GetString("GitRemoteCommandFailed"), exception.Message);
            }
        });
    }

    private void SelectRemote(object? parameter)
    {
        if (parameter is not RemoteViewItem remoteItem)
            return;
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        if (repository is null)
            return;

        _mainWindowViewModel.SelectRemote(remoteItem.Name);
        UpdateCommandStates();
        if (!IsGitOperationRunning)
        {
            _ = LoadRemoteDetailsAsync(repository);
        }
    }

    private async Task RenameRemoteAsync(object? parameter)
    {
        if (parameter is not RemoteViewItem remoteItem)
            return;

        ClearResultMessages();
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        GitRemote remote = remoteItem.Remote;
        if (repository is null)
        {
            return;
        }

        string currentName = remote.Name;
        string? newName = await _dialogService.ShowTextInputAsync(new TextInputDialogRequest(
            string.Format(_localizationService.GetString("RenameRemoteDialogTitle"), currentName),
            _localizationService.GetString("RenameRemoteDialogTextBoxHeader"),
            currentName,
            _localizationService.GetString("RenameRemoteDialogPrimaryButton"),
            _localizationService.GetString("TextInputDialogCancelButton"),
            _localizationService.GetString("RemoteNamePlaceholder")));

        if (string.IsNullOrWhiteSpace(newName) || newName == currentName)
        {
            return;
        }

        await RunGitOperationAsync(string.Format(_localizationService.GetString("RenameRemoteProgress"), currentName, newName), async () =>
        {
            try
            {
                GitRemoteOperationResult result = await _gitService.Remotes.RenameRemoteAsync(repository, remote, newName);
                _mainWindowViewModel?.SelectRemote(newName);
                await LoadRemoteDetailsAsync(repository);
                string successMessage = string.Format(_localizationService.GetString("RemoteNameUpdated"), currentName);
                ShowSuccess(successMessage, result.Output);
            }
            catch (FileNotFoundException)
            {
                ShowError(_localizationService.GetString("GitExecutableNotFound"));
            }
            catch (DirectoryNotFoundException)
            {
                ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
            }
            catch (GitCommandException exception)
            {
                ShowError(_localizationService.GetString("GitRemoteCommandFailed"), exception.Message);
            }
        });
    }

    private async Task EditRemoteUrlAsync(object? parameter)
    {
        if (parameter is not RemoteViewItem remoteItem)
            return;

        ClearResultMessages();
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        GitRemote remote = remoteItem.Remote;
        if (repository is null)
        {
            return;
        }

        string currentName = remote.DisplayUrl;
        string? newUrl = await _dialogService.ShowTextInputAsync(new TextInputDialogRequest(
            string.Format(_localizationService.GetString("EditRemoteUrlDialogTitle"), remote.Name),
            _localizationService.GetString("EditRemoteUrlDialogTextBoxHeader"),
            currentName,
            _localizationService.GetString("EditRemoteUrlDialogPrimaryButton"),
            _localizationService.GetString("TextInputDialogCancelButton"),
            _localizationService.GetString("RemoteUrlPlaceholder")));

        if (string.IsNullOrWhiteSpace(newUrl) || newUrl == currentName)
        {
            return;
        }

        await RunGitOperationAsync(string.Format(_localizationService.GetString("EditRemoteUrlProgress"), remote.Name), async () =>
        {
            try
            {
                GitRemoteOperationResult result = await _gitService.Remotes.SetRemoteUrlAsync(repository, remote, newUrl);
                await LoadRemoteDetailsAsync(repository);
                string successMessage = string.Format(_localizationService.GetString("RemoteUrlUpdated"), remote.Name);
                ShowSuccess(successMessage, result.Output);
            }
            catch (FileNotFoundException)
            {
                ShowError(_localizationService.GetString("GitExecutableNotFound"));
            }
            catch (DirectoryNotFoundException)
            {
                ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
            }
            catch (GitCommandException exception)
            {
                ShowError(_localizationService.GetString("GitRemoteCommandFailed"), exception.Message);
            }
        });
    }

    private async Task RemoveRemoteAsync(object? parameter)
    {
        if (parameter is not RemoteViewItem remoteItem)
            return;
        
        ClearResultMessages();
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        GitRemote remote = remoteItem.Remote;
        if (repository is null)
        {
            return;
        }

        bool confirmed = await _dialogService.ConfirmAsync(
            string.Format(_localizationService.GetString("RemoveRemoteDialogTitle"), remote.Name),
            string.Format(_localizationService.GetString("RemoveRemoteDialogMessage"), remote.Name),
            _localizationService.GetString("RemoveRemoteDialogPrimaryButton"));
        if (!confirmed)
        {
            return;
        }

        await RunGitOperationAsync(string.Format(_localizationService.GetString("RemoveRemoteProgress"), remote.Name), async () =>
        {
            try
            {
                GitRemoteOperationResult result = await _gitService.Remotes.RemoveRemoteAsync(repository, remote);
                await LoadRemoteDetailsAsync(repository);
                string successMessage = string.Format(_localizationService.GetString("RemoteRemoved"), remote.Name);
                ShowSuccess(successMessage, result.Output);
            }
            catch (FileNotFoundException)
            {
                ShowError(_localizationService.GetString("GitExecutableNotFound"));
            }
            catch (DirectoryNotFoundException)
            {
                ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
            }
            catch (GitCommandException exception)
            {
                ShowError(_localizationService.GetString("GitRemoteCommandFailed"), exception.Message);
            }
        });
    }

    private async Task BrowseRepositorySearchStartPathAsync()
    {
        string? selectedPath = await _storagePickerService.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            RepositorySearchStartPath = selectedPath;
            _ = SearchRepositoriesAsync();
        }
    }

    private async Task SearchRepositoriesAsync()
    {
        ClearResultMessages();

        string startPath = RepositorySearchStartPath.Trim();
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return;
        }

        IsGitOperationRunning = true;
        ProgressMessage = _localizationService.GetString("SearchingRepositoriesProgress");
        try
        {
            IReadOnlyList<RepositoryInfo> repositories = await _gitService.RepositorySearch.SearchAsync(startPath);
            ReplaceFoundRepositories(repositories);
            _gitService.RepositorySearch.SaveFoundRepositories(repositories);
        }
        catch (DirectoryNotFoundException)
        {
            ShowError(_localizationService.GetString("RepositorySearchFolderNotFound"));
        }
        finally
        {
            IsGitOperationRunning = false;
        }
    }

    private async Task OpenFoundRepositoryAsync(RepositoryInfo? repository)
    {
        repository ??= SelectedFoundRepository;
        if (repository is not null)
        {
            await OpenRepositoryAsync(repository);
        }
    }

    private async Task OpenRecentRepositoryAsync(RepositoryInfo? repository)
    {
        if (repository is null)
        {
            return;
        }

        ClearResultMessages();

        RepositoryInfo? refreshedRepository = _gitService.RepositoryDiscovery.TryOpenRepository(repository.Path);
        if (refreshedRepository is null && !string.IsNullOrWhiteSpace(repository.MainWorktreePath))
        {
            refreshedRepository = _gitService.RepositoryDiscovery.TryOpenRepository(repository.MainWorktreePath);
        }
        if (refreshedRepository is null)
        {
            ShowError(_localizationService.GetString("RecentRepositoryCannotBeOpened"));
            return;
        }

        await OpenRepositoryAsync(refreshedRepository);
    }

    public async Task RefreshCurrentRepositoryAsync()
    {
        ClearResultMessages();

        if (_mainWindowViewModel.CurrentRepository is null)
        {
            ClearRepository();
            return;
        }

        RepositoryInfo? refreshedRepository = RefreshCurrentRepositoryIdentity();
        if (refreshedRepository is null)
        {
            ShowError(_localizationService.GetString("RecentRepositoryCannotBeOpened"));
            return;
        }

        await LoadRepositoryDetailsAsync(refreshedRepository);
    }

    public RepositoryInfo? RefreshCurrentRepositoryIdentity()
    {
        RepositoryInfo? currentRepository = _mainWindowViewModel.CurrentRepository;
        if (currentRepository is null)
        {
            return null;
        }

        RepositoryInfo? refreshedRepository = _gitService.RepositoryDiscovery.TryOpenRepository(currentRepository.Path);
        if (refreshedRepository is null)
        {
            return null;
        }

        UpdateRepositoryIdentity(refreshedRepository);
        CurrentBranch = refreshedRepository.CurrentBranch;
        _mainWindowViewModel.UpdateCurrentRepositoryInfo(refreshedRepository);
        return refreshedRepository;
    }

    public async Task RefreshSelectedRemoteAsync()
    {
        RepositoryInfo? currentRepository = _mainWindowViewModel.CurrentRepository;
        if (currentRepository is null)
        {
            return;
        }

        await LoadRemoteDetailsAsync(currentRepository);
    }

    private async Task OpenRepositoryAsync(RepositoryInfo repository)
    {
        UpdateRepositoryIdentity(repository);
        CurrentBranch = repository.CurrentBranch;

        IReadOnlyList<RepositoryInfo> recentRepositories = _recentRepositoriesService.Add(repository);
        _mainWindowViewModel.SetCurrentRepository(repository, recentRepositories);
        OnPropertyChanged(nameof(IsRepositoryOpen));
        UpdateCommandStates();

        await LoadRepositoryDetailsAsync(repository);
    }

    public async Task<bool> OpenRepositoryPathAsync(string path)
    {
        RepositoryInfo? repository = _gitService.RepositoryDiscovery.TryOpenRepository(path);
        if (repository is null)
        {
            ShowError(_localizationService.GetString("RecentRepositoryCannotBeOpened"));
            return false;
        }

        await OpenRepositoryAsync(repository);
        return true;
    }

    private void CloseRepository()
    {
        ClearResultMessages();
        _mainWindowViewModel.CloseCurrentRepository();
        ClearRepository();
        UpdateCommandStates();
    }

    public void UpdateCurrentBranch(string branchName)
    {
        CurrentBranch = branchName;
    }

    private async Task LoadRepositoryDetailsAsync(RepositoryInfo repository)
    {
        try
        {
            CurrentUser = await _gitService.Configuration.GetUserNameAsync(ConfigScope.None, repository);
            CurrentEmail = await _gitService.Configuration.GetUserEmailAsync(ConfigScope.None, repository);
        }
        catch
        {
            CurrentUser = "";
            CurrentEmail = "";
        }

        await LoadStatusAsync(repository);
        await LoadLastCommitAsync(repository);
        await LoadRemoteDetailsAsync(repository);
        await LoadWorktreesAsync(repository);
    }

    private async Task LoadWorktreesAsync(RepositoryInfo repository)
    {
        try
        {
            IReadOnlyList<GitWorktree> worktrees = await _gitService.GetWorktreesAsync(repository);
            Worktrees.Clear();
            CurrentWorktreeItem = null;
            foreach (GitWorktree worktree in worktrees.Where(item => !item.IsBare))
            {
                var worktreeItem = new WorktreeViewItem(worktree,
                                                        _localizationService,
                                                        _asyncCommandExecutor,
                                                        OpenWorktreeFolder,
                                                        item => OpenWorktreeAsync(item),
                                                        item => MoveWorktreeAsync(item),
                                                        item => RemoveWorktreeAsync(item),
                                                        item => ToggleWorktreeLockAsync(item));
                Worktrees.Add(worktreeItem);
                if (worktree.IsCurrent)
                    CurrentWorktreeItem = worktreeItem;
            }
        }
        catch (Exception exception) when (exception is GitCommandException or FileNotFoundException or DirectoryNotFoundException)
        {
            Worktrees.Clear();
            CurrentWorktreeItem = null;
            ShowError(_localizationService.GetString("WorktreeListFailed"), exception.Message);
        }

        OnPropertyChanged(nameof(HasNoWorktrees));
    }

    private async Task CreateWorktreeAsync()
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        if (repository is null)
        {
            return;
        }

        string mainWorktreePath = string.IsNullOrWhiteSpace(repository.MainWorktreePath)
            ? repository.Path
            : repository.MainWorktreePath;
        string parentPath = Directory.GetParent(mainWorktreePath)?.FullName ?? mainWorktreePath;
        string repositoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(mainWorktreePath));
        string defaultPath = Path.Combine(parentPath, $"{repositoryName}-worktree");
        string detachedStartPoint = repository.CurrentBranch.StartsWith("Detached at ", StringComparison.Ordinal)
            ? repository.CurrentBranch["Detached at ".Length..]
            : "HEAD";
        string startPoint = repository.IsDetachedHead
            ? Worktrees.FirstOrDefault(item => item.Worktree.IsCurrent)?.Worktree.HeadHash ?? detachedStartPoint
            : repository.CurrentBranch;
        WorktreeCreationRequest? request = await _dialogService.ShowCreateWorktreeDialogAsync(
            repository,
            defaultPath,
            startPoint,
            creationMode: WorktreeCreationMode.Detached,
            startPointKind: repository.IsDetachedHead
                ? GitRevisionKind.Commit
                : GitRevisionKind.Branch);
        if (request is null)
        {
            return;
        }

        if (request.CreationMode == WorktreeCreationMode.ExistingBranch)
        {
            string branchName = request.StartPoint.StartsWith("refs/heads/", StringComparison.Ordinal)
                ? request.StartPoint[11..]
                : request.StartPoint;
            IReadOnlyList<GitWorktree> worktrees = await _gitService.GetWorktreesAsync(repository);
            GitWorktree? occupiedWorktree = worktrees.FirstOrDefault(item =>
                string.Equals(item.BranchName, branchName, StringComparison.Ordinal));
            if (occupiedWorktree is not null)
            {
                ShowError(
                    _localizationService.GetString("WorktreeBranchAlreadyCheckedOut"),
                    occupiedWorktree.Path);
                return;
            }
        }

        await ExecuteWorktreeOperationAsync(
            _localizationService.GetString("CreatingWorktreeProgress"),
            async () =>
            {
                await _gitService.Worktrees.AddAsync(repository, request);
                await OpenRepositoryPathAsync(request.Path);
                ShowSuccess(string.Format(_localizationService.GetString("WorktreeCreated"), request.Path));
            });
    }

    private Task OpenWorktreeAsync(object? parameter)
    {
        return parameter is WorktreeViewItem item && item.CanOpen
            ? OpenRepositoryPathAsync(item.Path)
            : Task.CompletedTask;
    }

    private void OpenWorktreeFolder(WorktreeViewItem item)
    {
        try
        {
            _fileExplorerService.OpenFolder(item.Path);
        }
        catch (Exception exception) when (exception is DirectoryNotFoundException or InvalidOperationException or Win32Exception)
        {
            ShowError(_localizationService.GetString("WorktreeFolderOpenFailed"), exception.Message);
        }
    }

    private async Task MoveWorktreeAsync(object? parameter)
    {
        if (parameter is not WorktreeViewItem { CanMove: true } item ||
            _mainWindowViewModel.CurrentRepository is not RepositoryInfo repository)
        {
            return;
        }

        string? newPath = await _dialogService.ShowTextInputAsync(new TextInputDialogRequest(
            _localizationService.GetString("MoveWorktreeDialogTitle"),
            _localizationService.GetString("MoveWorktreePathHeader"),
            item.Path,
            _localizationService.GetString("MoveWorktreeButton"),
            _localizationService.GetString("ConfirmationDialogCancelButton")));
        if (string.IsNullOrWhiteSpace(newPath) ||
            string.Equals(newPath, item.Path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await ExecuteWorktreeOperationAsync(
            _localizationService.GetString("MovingWorktreeProgress"),
            async () =>
            {
                await _gitService.Worktrees.MoveAsync(repository, item.Worktree, newPath);
                if (item.Worktree.IsCurrent)
                {
                    await OpenRepositoryPathAsync(newPath);
                }
                else
                {
                    await LoadWorktreesAsync(repository);
                }

                ShowSuccess(_localizationService.GetString("WorktreeMoved"));
            });
    }

    private async Task RemoveWorktreeAsync(object? parameter)
    {
        if (parameter is not WorktreeViewItem { CanRemove: true } item ||
            _mainWindowViewModel.CurrentRepository is not RepositoryInfo repository)
        {
            return;
        }

        bool confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("RemoveWorktreeDialogTitle"),
            string.Format(_localizationService.GetString("RemoveWorktreeDialogMessage"), item.Path),
            _localizationService.GetString("RemoveWorktreeButton"));
        if (!confirmed)
        {
            return;
        }

        await ExecuteWorktreeOperationAsync(
            _localizationService.GetString("RemovingWorktreeProgress"),
            async () =>
            {
                bool wasCurrent = item.Worktree.IsCurrent;
                await _gitService.Worktrees.RemoveAsync(repository, item.Worktree, false);
                if (wasCurrent)
                {
                    await OpenRepositoryPathAsync(repository.MainWorktreePath);
                }
                else
                {
                    await LoadWorktreesAsync(repository);
                }

                ShowSuccess(_localizationService.GetString("WorktreeRemoved"));
            });
    }

    private async Task ToggleWorktreeLockAsync(object? parameter)
    {
        if (parameter is not WorktreeViewItem item ||
            _mainWindowViewModel.CurrentRepository is not RepositoryInfo repository)
        {
            return;
        }

        await ExecuteWorktreeOperationAsync(
            item.Worktree.IsLocked
                ? _localizationService.GetString("UnlockingWorktreeProgress")
                : _localizationService.GetString("LockingWorktreeProgress"),
            async () =>
            {
                if (item.Worktree.IsLocked)
                {
                    await _gitService.Worktrees.UnlockAsync(repository, item.Worktree);
                }
                else
                {
                    await _gitService.Worktrees.LockAsync(repository, item.Worktree, "Managed by SimpleGit11");
                }

                await LoadWorktreesAsync(repository);
                ShowSuccess(item.Worktree.IsLocked
                    ? _localizationService.GetString("WorktreeUnlocked")
                    : _localizationService.GetString("WorktreeLocked"));
            });
    }

    private async Task PruneWorktreesAsync()
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        if (repository is null)
        {
            return;
        }

        string preview;
        try
        {
            preview = await _gitService.Worktrees.GetPrunePreviewAsync(repository);
        }
        catch (GitCommandException exception)
        {
            ShowError(_localizationService.GetString("WorktreePruneFailed"), exception.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(preview))
        {
            ShowSuccess(_localizationService.GetString("NoWorktreesToPrune"));
            return;
        }

        bool confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("PruneWorktreesDialogTitle"),
            preview.Trim(),
            _localizationService.GetString("PruneWorktreesButton"));
        if (!confirmed)
        {
            return;
        }

        await ExecuteWorktreeOperationAsync(
            _localizationService.GetString("PruningWorktreesProgress"),
            async () =>
            {
                await _gitService.Worktrees.PruneAsync(repository);
                await LoadWorktreesAsync(repository);
                ShowSuccess(_localizationService.GetString("WorktreesPruned"));
            });
    }

    private Task RepairWorktreesAsync()
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        return repository is null
            ? Task.CompletedTask
            : ExecuteWorktreeOperationAsync(
                _localizationService.GetString("RepairingWorktreesProgress"),
                async () =>
                {
                    await _gitService.Worktrees.RepairAsync(repository);
                    await LoadWorktreesAsync(repository);
                    ShowSuccess(_localizationService.GetString("WorktreesRepaired"));
                });
    }

    private async Task ExecuteWorktreeOperationAsync(string progressMessage, Func<Task> operation)
    {
        await RunGitOperationAsync(progressMessage, async () =>
        {
            try
            {
                await operation();
            }
            catch (Exception exception) when (exception is GitCommandException or FileNotFoundException or DirectoryNotFoundException)
            {
                ShowError(_localizationService.GetString("WorktreeOperationFailed"), exception.Message);
            }
        });
    }

    private async Task LoadStatusAsync(RepositoryInfo repository)
    {
        try
        {
            GitStatusSnapshot snapshot = await _gitService.GetStatusAsync(repository);
            StagedCount = snapshot.StagedChanges.Count;
            UnstagedCount = snapshot.UnstagedChanges.Count;
            ConflictCount = snapshot.ConflictedChanges.Count;

            if (ConflictCount > 0)
            {
                RepositoryState = string.Format(_localizationService.GetString("RepositoryStateConflicts"), ConflictCount);
            }
            else if (StagedCount == 0 && UnstagedCount == 0)
            {
                RepositoryState = _localizationService.GetString("RepositoryStateClean");
            }
            else
            {
                RepositoryState = string.Format(
                    _localizationService.GetString("RepositoryStateChanged"),
                    StagedCount,
                    UnstagedCount);
            }
        }
        catch (FileNotFoundException)
        {
            ShowError(_localizationService.GetString("GitExecutableNotFound"));
        }
        catch (DirectoryNotFoundException)
        {
            ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
        }
        catch (GitCommandException exception)
        {
            ShowError(_localizationService.GetString("GitStatusCommandFailed"), exception.Message);
        }
    }

    private async Task LoadLastCommitAsync(RepositoryInfo repository)
    {
        try
        {
            GitCommit commit = await _gitService.GetLastCommitAsync(repository);
            if (string.IsNullOrWhiteSpace(commit.Hash))
            {
                LastCommitSummary = _localizationService.GetString("NoCommitsYet");
                LastCommitDetails = "";
                return;
            }

            LastCommitSummary = $"{commit.ShortHash} {commit.Title}";
            LastCommitDetails = string.IsNullOrWhiteSpace(commit.DisplayDate)
                ? commit.DisplayAuthor
                : $"{commit.DisplayAuthor} - {commit.DisplayDate}";
        }
        catch (FileNotFoundException)
        {
            ShowError(_localizationService.GetString("GitExecutableNotFound"));
        }
        catch (DirectoryNotFoundException)
        {
            ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
        }
        catch (GitCommandException exception)
        {
            ShowError(_localizationService.GetString("GitLogCommandFailed"), exception.Message);
        }
    }

    private async Task LoadRemoteDetailsAsync(RepositoryInfo repository)
    {
        try
        {
            IReadOnlyList<GitRemote> remotes = await _gitService.GetRemotesAsync(repository);
            _remotes = remotes.ToList();
            LoadRemoteViewItems(remotes);
            GitCurrentBranchRemoteStatus status =
                await _gitService.GetCurrentBranchRemoteStatusAsync(
                    repository,
                    CurrentRemoteItem?.Remote);
            GitRemoteBranchStatus? trackingTarget = status.TrackingTarget;
            if (trackingTarget is null)
            {
                TrackingSummary = _localizationService.GetString("NoUpstreamConfigured");
                TrackingUrl = "";
                TrackingRemoteName = "";
            }
            else
            {
                GitRemote? trackingRemote = remotes.FirstOrDefault(
                    remote => remote.Name == trackingTarget.RemoteName);
                TrackingRemoteName = trackingRemote?.Name ?? "";
                TrackingSummary = FormatRemoteBranchSummary(trackingTarget);
                TrackingUrl = trackingRemote?.DisplayUrl ?? "";
            }

            GitRemoteBranchStatus? pushTarget = status.PushTarget;
            HasPushTarget = pushTarget is not null;
            if (pushTarget is null)
            {
                PushSummary = "";
                PushUrl = "";
            }
            else
            {
                GitRemote? pushRemote = remotes.FirstOrDefault(
                    remote => remote.Name == pushTarget.RemoteName);
                PushSummary = FormatRemoteBranchSummary(pushTarget);
                PushUrl = pushRemote?.DisplayUrl ?? "";
            }
        }
        catch (FileNotFoundException)
        {
            ShowError(_localizationService.GetString("GitExecutableNotFound"));
        }
        catch (DirectoryNotFoundException)
        {
            ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
        }
        catch (GitCommandException exception)
        {
            ShowError(_localizationService.GetString("GitRemoteCommandFailed"), exception.Message);
        }
    }

    private void LoadRemoteViewItems(IReadOnlyList<GitRemote> remotes)
    {
        string? selectedRemoteName = string.IsNullOrWhiteSpace(_mainWindowViewModel.SelectedRemoteName)
            ? CurrentRemoteItem?.Name
            : _mainWindowViewModel.SelectedRemoteName;
        RemoteViewItems.Clear();
        CurrentRemoteItem = null;
        foreach (GitRemote remote in remotes)
        {
            var remoteItem = new RemoteViewItem(remote,
                                                _localizationService,
                                                _asyncCommandExecutor,
                                                item => SelectRemote(item),
                                                item => RenameRemoteAsync(item),
                                                item => EditRemoteUrlAsync(item),
                                                item => RemoveRemoteAsync(item),
                                                selectedRemoteName?.Equals(remote.Name) ?? false);
            RemoteViewItems.Add(remoteItem);
            if (remoteItem.IsCurrent)
                CurrentRemoteItem = remoteItem;
        }

        OnPropertyChanged(nameof(HasNoRemotes));
    }

    private async Task RunGitOperationAsync(string progressMessage, Func<Task> operation)
    {
        await _gitService.ExecuteAsync(async () =>
        {
            IsGitOperationRunning = true;
            ProgressMessage = progressMessage;
            try
            {
                await operation();
            }
            finally
            {
                ProgressMessage = "";
                IsGitOperationRunning = false;
            }
        });
    }

    private void ClearRepository()
    {
        RepositoryName = _localizationService.GetString("NoRepositoryOpen");
        RepositoryPath = "";
        CurrentBranch = _localizationService.GetString("NoBranch");
        CurrentUser = "";
        CurrentEmail = "";
        ResetRepositoryDetails();
        OnPropertyChanged(nameof(IsRepositoryOpen));
    }

    private void UpdateRepositoryIdentity(RepositoryInfo repository)
    {
        string mainRepositoryPath = string.IsNullOrWhiteSpace(repository.MainWorktreePath)
            ? repository.Path
            : repository.MainWorktreePath;
        DirectoryInfo mainRepositoryDirectory = new(Path.TrimEndingDirectorySeparator(mainRepositoryPath));
        RepositoryName = mainRepositoryDirectory.Name;
        RepositoryPath = mainRepositoryPath;
    }

    private void ResetRepositoryDetails()
    {
        RepositoryState = _localizationService.GetString("RepositoryStateEmpty");
        LastCommitSummary = _localizationService.GetString("NoCommitSelected");
        LastCommitDetails = "";
        TrackingSummary = _localizationService.GetString("NoUpstreamConfigured");
        TrackingUrl = "";
        TrackingRemoteName = "";
        PushSummary = "";
        PushUrl = "";
        HasPushTarget = false;
        _remotes = [];
        StagedCount = 0;
        UnstagedCount = 0;
        ConflictCount = 0;
        Worktrees.Clear();
        RemoteViewItems.Clear();
        OnPropertyChanged(nameof(HasNoWorktrees));
    }

    private string FormatRemoteBranchSummary(GitRemoteBranchStatus target)
    {
        if (!target.IsPublished)
        {
            return string.Format(
                _localizationService.GetString("RepositoryRemoteBranchNotPublished"),
                target.TrackingBranch);
        }

        return string.Format(
            _localizationService.GetString("RepositoryRemoteBranchSummary"),
            target.TrackingBranch,
            target.AheadCount,
            target.BehindCount);
    }

    private void ClearResultMessages()
    {
        ClearNotification();
    }

    private void ShowError(string message)
    {
        ShowNotification(AppNotificationSeverity.Error, message);
    }

    private void ShowError(string message, string? details)
    {
        ShowNotification(AppNotificationSeverity.Error, message, details);
    }

    private void ShowSuccess(string message)
    {
        ShowNotification(AppNotificationSeverity.Success, message);
    }

    private void ShowSuccess(string message, string? details)
    {
        ShowNotification(AppNotificationSeverity.Success, message, details);
    }

    private void UpdateCommandStates()
    {
        OpenRepositoryCommand.NotifyCanExecuteChanged();
        CreateRepositoryCommand.NotifyCanExecuteChanged();
        CloneRepositoryCommand.NotifyCanExecuteChanged();
        AddRemoteCommand.NotifyCanExecuteChanged();
        RemoveRemoteCommand.NotifyCanExecuteChanged();
        CloseRepositoryCommand.NotifyCanExecuteChanged();
        RefreshRepositoryCommand.NotifyCanExecuteChanged();
        OpenFoundRepositoryCommand.NotifyCanExecuteChanged();
        OpenRecentRepositoryCommand.NotifyCanExecuteChanged();
        BrowseRepositorySearchStartPathCommand.NotifyCanExecuteChanged();
        SearchRepositoriesCommand.NotifyCanExecuteChanged();
        CreateWorktreeCommand.NotifyCanExecuteChanged();
        PruneWorktreesCommand.NotifyCanExecuteChanged();
        RepairWorktreesCommand.NotifyCanExecuteChanged();
        ArchiveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRunGitOperation));
        OnPropertyChanged(nameof(CanRemoveTrackingRemote));
    }

    private void LoadPersistedFoundRepositories()
    {
        ReplaceFoundRepositories(_gitService.RepositorySearch.LoadFoundRepositories());
    }

    private void ReplaceFoundRepositories(IReadOnlyList<RepositoryInfo> repositories)
    {
        string? selectedPath = SelectedFoundRepository?.Path;
        FoundRepositories.Clear();
        _foundRepositoryItems.Clear();
        foreach (RepositoryInfo repository in repositories)
        {
            FoundRepositories.Add(repository);
            _foundRepositoryItems.Add(new FoundRepositoryViewItem(
                repository,
                _asyncCommandExecutor,
                async path => await OpenRepositoryPathAsync(path),
                _clipboardService.SetText));
        }

        SelectedFoundRepository = FoundRepositories.FirstOrDefault(
            repository => string.Equals(repository.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
        ApplyFoundRepositoryFilter();
        OnPropertyChanged(nameof(HasNoFoundRepositories));
    }

    private void ApplyFoundRepositoryFilter()
    {
        string filter = FoundRepositoryFilterText.Trim();

        IEnumerable<FoundRepositoryViewItem> repositories = _foundRepositoryItems;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            repositories = repositories.Where(repository =>
                repository.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || repository.Path.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        FilteredFoundRepositories.Clear();
        foreach (FoundRepositoryViewItem repository in repositories)
        {
            FilteredFoundRepositories.Add(repository);
        }

        OnPropertyChanged(nameof(HasNoFilteredFoundRepositories));
    }

    private async Task<string?> GetRemoteNameForNewRemoteAsync(IReadOnlyList<GitRemote> remotes)
    {
        if (remotes.Count == 0)
        {
            return "origin";
        }

        string defaultRemoteName = GetDefaultRemoteName(remotes);
        string? remoteName = await _dialogService.ShowTextInputAsync(new TextInputDialogRequest(
            _localizationService.GetString("AddRemoteNameDialogTitle"),
            _localizationService.GetString("AddRemoteNameDialogTextBoxHeader"),
            defaultRemoteName,
            _localizationService.GetString("AddRemoteNameDialogPrimaryButton"),
            _localizationService.GetString("TextInputDialogCancelButton"),
            _localizationService.GetString("AddRemoteNameDialogPlaceholder")));

        if (string.IsNullOrWhiteSpace(remoteName))
        {
            return null;
        }

        if (!IsValidRemoteName(remoteName))
        {
            ShowError(_localizationService.GetString("RemoteNameInvalid"));
            return null;
        }

        if (remotes.Any(remote => string.Equals(remote.Name, remoteName, StringComparison.Ordinal)))
        {
            ShowError(string.Format(_localizationService.GetString("RemoteAlreadyExists"), remoteName));
            return null;
        }

        return remoteName;
    }

    private static string CreateArchiveName(RepositoryInfo repository, string startPoint)
    {
        string revisionName = string.Equals(startPoint, "HEAD", StringComparison.OrdinalIgnoreCase)
            ? repository.CurrentBranch
            : startPoint;
        if (revisionName.Length == 40 && revisionName.All(Uri.IsHexDigit))
        {
            revisionName = revisionName[..8];
        }

        string name = $"{repository.Name}-{revisionName}";
        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        char[] sanitizedCharacters = name
            .Select(character =>
                invalidCharacters.Contains(character) || character is '/' or '\\'
                    ? '-'
                    : character)
            .ToArray();
        string sanitizedName = new string(sanitizedCharacters).Trim(' ', '.', '-');
        return string.IsNullOrWhiteSpace(sanitizedName)
            ? $"{repository.Name}-archive"
            : sanitizedName;
    }

    private static string GetDefaultRemoteName(IReadOnlyList<GitRemote> remotes)
    {
        if (remotes.All(remote => !string.Equals(remote.Name, "origin", StringComparison.Ordinal)))
        {
            return "origin";
        }

        if (remotes.All(remote => !string.Equals(remote.Name, "upstream", StringComparison.Ordinal)))
        {
            return "upstream";
        }

        int index = remotes.Count + 1;
        string name;
        do
        {
            name = $"remote-{index++}";
        }
        while (remotes.Any(remote => string.Equals(remote.Name, name, StringComparison.Ordinal)));

        return name;
    }

    private static bool IsValidRemoteName(string remoteName)
    {
        return !string.IsNullOrWhiteSpace(remoteName)
            && !remoteName.StartsWith("-", StringComparison.Ordinal)
            && !remoteName.Any(char.IsWhiteSpace)
            && !remoteName.Contains("..", StringComparison.Ordinal)
            && !remoteName.Contains("@{", StringComparison.Ordinal)
            && remoteName.IndexOfAny(['~', '^', ':', '?', '*', '[', '\\']) < 0;
    }
}
