using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SimpleGit11.ViewModels;

public sealed partial class HistoryViewModel : CommitBrowserViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly RepositoryViewModel _repositoryViewModel;
    private int _nextHistoryOffset;
    public HistoryViewModel(
        MainWindowViewModel mainWindowViewModel,
        RepositoryViewModel repositoryViewModel,
        IGitService gitService,
        ILocalizationService localizationService,
        IClipboardService clipboardService,
        IAsyncCommandExecutor asyncCommandExecutor,
        IDialogService dialogService,
        ISettingsService settingsService,
        IMessenger messenger)
        : base(
            mainWindowViewModel,
            gitService,
            localizationService,
            clipboardService,
            asyncCommandExecutor,
            settingsService,
            messenger,
            "SelectCommitToViewDiff",
            "SelectCommitFileToViewDiff",
            "OpenRepositoryBeforeHistory")
    {
        _dialogService = dialogService;
        _repositoryViewModel = repositoryViewModel;
        ProgressMessage = "";
    }

    public override Visibility EditCommitMessageActionVisibility => Visibility.Visible;

    public bool CanEditSelectedCommitMessage =>
        SelectedCommit is not null
        && SelectedCommit.IsSynchronized == false
        && AllCommits.FirstOrDefault()?.Hash == SelectedCommit.Hash
        && !IsGitOperationRunning;

    private bool CanRunHistoryOperation()
    {
        return SelectedCommit is not null && !IsGitOperationRunning;
    }

    [RelayCommand(CanExecute = nameof(CanRefreshHistory), FlowExceptionsToTaskScheduler = true)]
    private Task OnRefreshHistoryAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(RefreshHistoryAsync);
    }

    private bool CanRefreshHistory()
    {
        return !IsGitOperationRunning;
    }

    [RelayCommand(CanExecute = nameof(CanLoadMoreHistory), FlowExceptionsToTaskScheduler = true)]
    private Task OnLoadMoreHistoryAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(LoadMoreHistoryAsync);
    }

    private bool CanLoadMoreHistory()
    {
        return HasMoreCommits && !IsGitOperationRunning;
    }

    [RelayCommand(CanExecute = nameof(CanRunHistoryOperation), FlowExceptionsToTaskScheduler = true)]
    private Task OnRevertCommitAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(RevertCommitCoreAsync);
    }

    [RelayCommand(CanExecute = nameof(CanRunHistoryOperation), FlowExceptionsToTaskScheduler = true)]
    private Task OnCheckoutCommitAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(CheckoutCommitCoreAsync);
    }

    [RelayCommand(CanExecute = nameof(CanRunHistoryOperation), FlowExceptionsToTaskScheduler = true)]
    private Task OnResetSoftAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(() => ResetAsync("soft"));
    }

    [RelayCommand(CanExecute = nameof(CanRunHistoryOperation), FlowExceptionsToTaskScheduler = true)]
    private Task OnResetMixedAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(() => ResetAsync("mixed"));
    }

    [RelayCommand(CanExecute = nameof(CanRunHistoryOperation), FlowExceptionsToTaskScheduler = true)]
    private Task OnResetHardAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(() => ResetAsync("hard"));
    }

    [RelayCommand(CanExecute = nameof(CanRepairRepository), FlowExceptionsToTaskScheduler = true)]
    private Task OnRepairRepositoryAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(RepairRepositoryCoreAsync);
    }

    private bool CanRepairRepository()
    {
        return HasRepositoryRecoveryAction && !IsGitOperationRunning;
    }

    protected override bool CanEditCommitMessage()
    {
        return CanEditSelectedCommitMessage;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperationProgressVisibility))]
    public partial string ProgressMessage { get; private set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperationProgressVisibility))]
    public partial bool IsGitOperationRunning { get; private set; }

    public Visibility OperationProgressVisibility =>
        IsGitOperationRunning && !string.IsNullOrWhiteSpace(ProgressMessage)
            ? Visibility.Visible
            : Visibility.Collapsed;

    partial void OnProgressMessageChanged(string value)
    {
        PublishOperationState(IsGitOperationRunning, ProgressMessage);
    }

    partial void OnIsGitOperationRunningChanged(bool value)
    {
        UpdateCommandStates();
        PublishOperationState(value, ProgressMessage);
    }

    [ObservableProperty]
    public partial bool HasNoCommits { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreHistoryCommand))]
    public partial bool HasMoreCommits { get; private set; }

    public Visibility HistoryVisible => AllCommits.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepositoryRecoveryActionVisibility))]
    [NotifyPropertyChangedFor(nameof(RepositoryRecoveryActionText))]
    [NotifyCanExecuteChangedFor(nameof(RepairRepositoryCommand))]
    public partial bool HasRepositoryRecoveryAction { get; private set; }

    public Visibility RepositoryRecoveryActionVisibility => HasRepositoryRecoveryAction ? Visibility.Visible : Visibility.Collapsed;

    public string RepositoryRecoveryActionText => HasRepositoryRecoveryAction
        ? _localizationService.GetString("RepairRepositoryActionText")
        : "";

    public async Task RefreshHistoryAsync()
    {
        await RunGitOperationAsync(async () =>
        {
            await RefreshHistoryCoreAsync();
        });
    }

    private async Task RefreshHistoryCoreAsync()
    {
        ClearError();
        HasNoCommits = false;
        ClearCommitDetails();

        if (_mainWindowViewModel.CurrentRepository is null)
        {
            ClearCommits();
            _nextHistoryOffset = 0;
            HasMoreCommits = false;
            ShowError(_localizationService.GetString("OpenRepositoryBeforeHistory"));
            OnPropertyChanged(nameof(HistoryVisible));
            return;
        }

        try
        {
            IsRefreshing = true;
            ProgressMessage = _localizationService.GetString("RefreshingHistoryProgress");
            GitCommitPage page = await _gitService.GetHistoryPageAsync(
                _mainWindowViewModel.CurrentRepository,
                0,
                CommitPageSize);
            ReplaceHistory(page);
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
            ShowHistoryError(exception);
        }
        finally
        {
            IsRefreshing = false;
            ProgressMessage = "";
            OnPropertyChanged(nameof(HistoryVisible));
        }
    }

    private async Task LoadMoreHistoryAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null || !HasMoreCommits)
        {
            return;
        }

        RepositoryInfo repository = _mainWindowViewModel.CurrentRepository;
        await RunGitOperationAsync(async () =>
        {
            try
            {
                ClearError();
                ProgressMessage = _localizationService.GetString("LoadingMoreHistoryProgress");
                GitCommitPage page = await _gitService.GetHistoryPageAsync(
                    repository,
                    _nextHistoryOffset,
                    CommitPageSize);
                _nextHistoryOffset += page.Commits.Count;
                AppendCommits(page.Commits);
                HasMoreCommits = page.HasMore;
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
                ShowHistoryError(exception);
            }
            finally
            {
                ProgressMessage = "";
            }
        });
    }

    private void ReplaceHistory(GitCommitPage page)
    {
        ReplaceCommits(page.Commits);
        _nextHistoryOffset = page.Commits.Count;
        HasMoreCommits = page.HasMore;
    }

    private async Task RepairRepositoryCoreAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeHistory"));
            return;
        }

        RepositoryInfo repository = _mainWindowViewModel.CurrentRepository;
        await RunGitOperationAsync(async () =>
        {
            try
            {
                ClearError();
                IsRefreshing = true;
                ProgressMessage = _localizationService.GetString("RepairingRepositoryProgress");
                GitRepositoryRepairResult repairResult = await _gitService.RepositoryRepair.RepairMissingObjectsAsync(repository);
                if (!repairResult.ObjectsFetched)
                {
                    ShowError(_localizationService.GetString("RepositoryRepairNoRemotes"));
                    return;
                }

                GitCommitPage page = await _gitService.GetHistoryPageAsync(
                    repository,
                    0,
                    CommitPageSize);
                ReplaceHistory(page);
                ShowSuccess(_localizationService.GetString("RepositoryRepairSucceeded"));
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
                if (_gitService.RepositoryRepair.IsMissingObjectHistoryError(exception))
                {
                    ShowError(
                        _localizationService.GetString("RepositoryRepairIncompleteTitle"),
                        string.Format(
                            _localizationService.GetString("RepositoryRepairFailedMissingObject"),
                            exception.Message));
                }
                else
                {
                    ShowError(_localizationService.GetString("RepositoryRepairFailed"), exception.Message);
                }
            }
            finally
            {
                IsRefreshing = false;
                ProgressMessage = "";
                OnPropertyChanged(nameof(HistoryVisible));
            }
        });
    }

    protected override async Task EditCommitMessageCoreAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedCommit is null || !CanEditSelectedCommitMessage)
        {
            return;
        }

        GitCommit commit = SelectedCommit;
        IReadOnlyList<GitChangedFile> changedFiles = commit.ChangedFilePaths
            .Select(path => new GitChangedFile(path, "Changed"))
            .ToArray();
        CommitDialogResult? answer = await _dialogService.ShowCommitDialogAsync(
            CommitDialogRequest.CreateMessageEdit(commit.Message, changedFiles));
        string? message = answer?.Message?.Trim();

        if (string.IsNullOrWhiteSpace(message) || message == commit.Message)
        {
            return;
        }

        RepositoryInfo repository = _mainWindowViewModel.CurrentRepository;
        await RunGitOperationAsync(async () =>
        {
            try
            {
                ClearResultMessages();
                ProgressMessage = _localizationService.GetString("AmendingCommitMessageProgress");
                GitCommitOperationResult operationResult = await _gitService.CommitWorkflow.AmendAsync(
                    repository,
                    message);
                if (!operationResult.Completed)
                {
                    return;
                }

                await RefreshHistoryCoreAsync();
                ShowSuccess(_localizationService.GetString("CommitMessageAmended"));
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
                ShowError(_localizationService.GetString("GitCommitCommandFailed"), exception.Message);
            }
            finally
            {
                ProgressMessage = "";
            }
        });
    }

    private async Task RevertCommitCoreAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedCommit is null)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeHistory"));
            return;
        }

        var repository = _mainWindowViewModel.CurrentRepository;
        var commit = SelectedCommit;
        var confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("RevertCommitDialogTitle"),
            string.Format(_localizationService.GetString("RevertCommitDialogMessage"), commit.ShortHash, commit.Title),
            _localizationService.GetString("RevertCommitDialogPrimaryButton"));

        if (!confirmed)
        {
            return;
        }

        await RunDangerousOperationAsync(
            () => _gitService.ChangeRecovery.RevertCommitAsync(repository, commit),
            string.Format(_localizationService.GetString("RevertCommitSucceeded"), commit.ShortHash),
            mayCreateConflicts: true);
    }

    private async Task CheckoutCommitCoreAsync()
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        GitCommit? commit = SelectedCommit;
        if (repository is null || commit is null)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeHistory"));
            return;
        }

        bool confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("CheckoutCommitDialogTitle"),
            string.Format(
                _localizationService.GetString("CheckoutCommitDialogMessage"),
                commit.ShortHash,
                commit.Title),
            _localizationService.GetString("CheckoutCommitDialogPrimaryButton"));
        if (!confirmed)
        {
            return;
        }

        await RunGitOperationAsync(async () =>
        {
            try
            {
                ClearResultMessages();
                ProgressMessage = string.Format(
                    _localizationService.GetString("CheckoutCommitProgressMessage"),
                    commit.ShortHash);
                await _gitService.Branches.CheckoutCommitAsync(repository, commit.Hash);
                await _repositoryViewModel.RefreshCurrentRepositoryIdentityAsync();
                await RefreshHistoryCoreAsync();
                ShowSuccess(string.Format(
                    _localizationService.GetString("CheckoutCommitSucceeded"),
                    commit.ShortHash));
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
                ShowError(_localizationService.GetString("CheckoutCommitFailed"), exception.Message);
            }
            finally
            {
                ProgressMessage = "";
            }
        });
    }

    private async Task ResetAsync(string mode)
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedCommit is null)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeHistory"));
            return;
        }

        var repository = _mainWindowViewModel.CurrentRepository;
        var commit = SelectedCommit;
        var titleKey = mode switch
        {
            "soft" => "ResetSoftDialogTitle",
            "mixed" => "ResetMixedDialogTitle",
            _ => "ResetHardDialogTitle"
        };
        var messageKey = mode switch
        {
            "soft" => "ResetSoftDialogMessage",
            "mixed" => "ResetMixedDialogMessage",
            _ => "ResetHardDialogMessage"
        };
        var primaryKey = mode switch
        {
            "soft" => "ResetSoftDialogPrimaryButton",
            "mixed" => "ResetMixedDialogPrimaryButton",
            _ => "ResetHardDialogPrimaryButton"
        };

        var confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString(titleKey),
            string.Format(_localizationService.GetString(messageKey), commit.ShortHash, commit.Title),
            _localizationService.GetString(primaryKey));

        if (!confirmed)
        {
            return;
        }

        await RunDangerousOperationAsync(
            () => _gitService.ChangeRecovery.ResetAsync(repository, commit, mode),
            string.Format(_localizationService.GetString("ResetSucceeded"), mode, commit.ShortHash));
    }

    private void ClearError()
    {
        HasRepositoryRecoveryAction = false;
        ClearNotification();
    }

    private void ShowError(string message)
    {
        ShowError(message, false);
    }

    private void ShowError(string message, string? details)
    {
        ShowError(message, details, false);
    }

    private void ShowError(string message, bool hasRepositoryRecoveryAction)
    {
        ShowError(message, null, hasRepositoryRecoveryAction);
    }

    private void ShowError(string message, string? details, bool hasRepositoryRecoveryAction)
    {
        HasRepositoryRecoveryAction = hasRepositoryRecoveryAction;
        ShowNotification(
            AppNotificationSeverity.Error,
            message,
            details,
            hasRepositoryRecoveryAction ? RepairRepositoryCommand : null,
            hasRepositoryRecoveryAction ? RepositoryRecoveryActionText : null);
    }

    private void ClearResultMessages()
    {
        HasRepositoryRecoveryAction = false;
        ClearNotification();
    }

    private void ShowSuccess(string message)
    {
        HasRepositoryRecoveryAction = false;
        ShowNotification(AppNotificationSeverity.Success, message);
    }

    private void ShowHistoryError(GitCommandException exception)
    {
        if (_gitService.RepositoryRepair.IsMissingObjectHistoryError(exception))
        {
            ShowError(
                _localizationService.GetString("RepositoryHistoryIncompleteTitle"),
                string.Format(
                    _localizationService.GetString("RepositoryMissingObjectHistoryError"),
                    exception.Message),
                true);
            return;
        }

        ShowError(_localizationService.GetString("GitLogCommandFailed"), exception.Message);
    }

    private async Task RunDangerousOperationAsync(
        System.Func<Task> operation,
        string successMessage,
        bool mayCreateConflicts = false)
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        await RunGitOperationAsync(async () =>
        {
            try
            {
                ClearResultMessages();
                await operation();
                await RefreshHistoryCoreAsync();
                ShowSuccess(successMessage);
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
                if (!mayCreateConflicts
                    || !await _mainWindowViewModel.TryShowConflictWarningAsync(repository, this, exception))
                {
                    ShowError(_localizationService.GetString("GitDangerousOperationFailed"), exception.Message);
                }
            }
        });
    }

    private async Task RunGitOperationAsync(System.Func<Task> operation)
    {
        await _gitService.ExecuteAsync(async () =>
        {
            IsGitOperationRunning = true;
            try
            {
                await operation();
            }
            finally
            {
                IsGitOperationRunning = false;
            }
        });
    }

    private void UpdateCommandStates()
    {
        RefreshHistoryCommand.NotifyCanExecuteChanged();
        LoadMoreHistoryCommand.NotifyCanExecuteChanged();
        CheckoutCommitCommand.NotifyCanExecuteChanged();
        RevertCommitCommand.NotifyCanExecuteChanged();
        ResetSoftCommand.NotifyCanExecuteChanged();
        ResetMixedCommand.NotifyCanExecuteChanged();
        ResetHardCommand.NotifyCanExecuteChanged();
        RaiseCommitDetailsCommandCanExecuteChanged();
        RepairRepositoryCommand.NotifyCanExecuteChanged();
        EditCommitMessageCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditSelectedCommitMessage));
    }

    protected override bool IsCommitDetailsOperationRunning => IsGitOperationRunning;

    protected override void ShowCommitDetailsError(string message, string? details = null)
    {
        ShowError(message, details);
    }

    protected override void ClearCommitDetailsError()
    {
        ClearError();
    }

    protected override void OnCommitFilterChanged()
    {
        HasNoCommits = AllCommits.Count == 0;
        OnPropertyChanged(nameof(CanEditSelectedCommitMessage));
    }

    protected override void OnBrowserSelectedCommitChanged()
    {
        OnPropertyChanged(nameof(CanEditSelectedCommitMessage));
        UpdateCommandStates();
    }

}
