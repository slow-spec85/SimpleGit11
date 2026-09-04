using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;

namespace SimpleGit11.ViewModels;

public sealed partial class CommitRangeViewModel : CommitBrowserViewModelBase
{
    private readonly IDialogService _dialogService;
    private System.Func<RepositoryInfo, int, int, Task<GitCommitPage>>? _loadCommitPageAsync;
    private CommitRangeCherryPickScope _cherryPickScope;
    private bool _hasCleanWorkingTree;
    private bool _hasOperationInProgress;
    private int _nextCommitOffset;

    public CommitRangeViewModel(
        MainWindowViewModel mainWindowViewModel,
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
        Title = "";
        Description = "";
        EmptyMessage = "";
        ProgressMessage = "";
    }

    [ObservableProperty]
    public partial string Title { get; private set; }

    [ObservableProperty]
    public partial string Description { get; private set; }

    [ObservableProperty]
    public partial string EmptyMessage { get; private set; }

    [ObservableProperty]
    public partial string ProgressMessage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; private set; }

    private void PublishCommitRangeOperationState()
    {
        PublishOperationState(IsLoading || IsLoadingMore, ProgressMessage);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    public partial bool HasNoCommits { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommitsCommand))]
    public partial bool HasMoreCommits { get; private set; }

    public bool ShowEmptyState => !IsLoading && HasNoCommits;

    public bool ShowContent => !IsLoading && HasUnfilteredCommits;

    public bool ShowCherryPickCommands => _cherryPickScope != CommitRangeCherryPickScope.None;

    public string CherryPickButtonText => SelectedCommits.Count > 1
        ? string.Format(
            _localizationService.GetString("CherryPickSelectedCount"),
            SelectedCommits.Count)
        : _localizationService.GetString("CherryPickAppBarButtonLabel");

    [ObservableProperty]
    public partial bool IsRevisionDiffMode { get; private set; }

    partial void OnProgressMessageChanged(string value)
    {
        PublishCommitRangeOperationState();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        RaiseCommitDetailsCommandCanExecuteChanged();
        UpdateCherryPickCommandStates();
        LoadMoreCommitsCommand.NotifyCanExecuteChanged();
        PublishCommitRangeOperationState();
    }

    partial void OnIsLoadingMoreChanged(bool value)
    {
        RaiseCommitDetailsCommandCanExecuteChanged();
        UpdateCherryPickCommandStates();
        LoadMoreCommitsCommand.NotifyCanExecuteChanged();
        PublishCommitRangeOperationState();
    }

    private bool CanCherryPickSelected()
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        return ShowCherryPickCommands
            && repository is not null
            && !repository.IsDetachedHead
            && !IsLoading
            && !IsLoadingMore
            && _hasCleanWorkingTree
            && !_hasOperationInProgress
            && SelectedCommits.Count > 0
            && GitCommitSelection.IsWithinScope(_cherryPickScope, SelectedCommits)
            && GitCommitSelection.CanApplyTogether(SelectedCommits);
    }

    [RelayCommand(CanExecute = nameof(CanCherryPickSelected), FlowExceptionsToTaskScheduler = true)]
    private Task OnCherryPickSelectedAsync() =>
        _asyncCommandExecutor.ExecuteAsync(() => CherryPickSelectedCoreAsync(GitCherryPickOptions.Default));

    [RelayCommand(CanExecute = nameof(CanCherryPickSelected), FlowExceptionsToTaskScheduler = true)]
    private Task OnCherryPickWithSourceAsync() =>
        _asyncCommandExecutor.ExecuteAsync(() => CherryPickSelectedCoreAsync(
            new GitCherryPickOptions(AppendSourceReference: true)));

    [RelayCommand(CanExecute = nameof(CanCherryPickSelected), FlowExceptionsToTaskScheduler = true)]
    private Task OnCherryPickWithSignOffAsync() =>
        _asyncCommandExecutor.ExecuteAsync(() => CherryPickSelectedCoreAsync(
            new GitCherryPickOptions(AddSignOff: true)));

    [RelayCommand(CanExecute = nameof(CanCherryPickSelected), FlowExceptionsToTaskScheduler = true)]
    private Task OnCherryPickWithoutCommitAsync() =>
        _asyncCommandExecutor.ExecuteAsync(() => CherryPickSelectedCoreAsync(
            new GitCherryPickOptions(NoCommit: true)));

    [RelayCommand(CanExecute = nameof(CanLoadMoreCommits), FlowExceptionsToTaskScheduler = true)]
    private Task OnLoadMoreCommitsAsync() =>
        _asyncCommandExecutor.ExecuteAsync(LoadMoreCommitsAsync);

    private bool CanLoadMoreCommits()
    {
        return HasMoreCommits
            && _loadCommitPageAsync is not null
            && !IsLoading
            && !IsLoadingMore;
    }

    public async Task InitializeAsync(CommitRangeNavigationArgs arguments)
    {
        IsRevisionDiffMode = false;
        SetCherryPickScope(arguments.CherryPickScope);
        ConfigureHeader(arguments);
        await LoadCommitsAsync((repository, skip, count) =>
            arguments.Direction == CommitRangeDirection.Incoming
                ? _gitService.Remotes.GetIncomingCommitsPageAsync(
                    repository,
                    arguments.Branch,
                    skip,
                    count)
                : _gitService.Remotes.GetOutgoingCommitsPageAsync(
                    repository,
                    arguments.Remote,
                    arguments.Branch,
                    skip,
                    count));
    }

    public async Task InitializeAsync(MergeCommitRangeNavigationArgs arguments)
    {
        IsRevisionDiffMode = false;
        SetCherryPickScope(CommitRangeCherryPickScope.None);
        Title = string.Format(
            _localizationService.GetString("MergedCommitRangeTitle"),
            arguments.MergeCommitShortHash);
        Description = _localizationService.GetString("MergedCommitRangeDescription");
        EmptyMessage = _localizationService.GetString("NoMergedCommits");
        await LoadCommitsAsync((repository, skip, count) => _gitService.Remotes.GetCommitsPageAsync(
            repository,
            $"{arguments.FirstParentHash}..{arguments.SecondParentHash}",
            skip,
            count));
    }

    public async Task InitializeAsync(RevisionRangeNavigationArgs arguments)
    {
        IsRevisionDiffMode = false;
        SetCherryPickScope(arguments.CherryPickScope);
        Title = arguments.Title;
        Description = arguments.Description;
        EmptyMessage = arguments.EmptyMessage;
        await LoadCommitsAsync((repository, skip, count) => arguments.IsTwoSidedComparison
            ? _gitService.Remotes.GetComparisonCommitsPageAsync(
                repository,
                arguments.LeftRevision,
                arguments.RightRevision,
                arguments.LeftLabel,
                arguments.RightLabel,
                skip,
                count)
            : _gitService.Remotes.GetCommitsPageAsync(
                repository,
                arguments.RevisionRange,
                skip,
                count));
    }

    public async Task InitializeAsync(RevisionDiffNavigationArgs arguments)
    {
        IsRevisionDiffMode = true;
        SetCherryPickScope(CommitRangeCherryPickScope.None);
        Title = arguments.Title;
        Description = arguments.Description;
        EmptyMessage = _localizationService.GetString("NoBranchFileChanges");
        await LoadCommitsAsync((_, _, _) => Task.FromResult(new GitCommitPage(
            [
                new GitCommit(
                    arguments.NewRevision,
                    arguments.NewRevision,
                    "",
                    "",
                    null,
                    arguments.Title,
                    arguments.Title,
                    diffBaseRevision: arguments.OldRevision)
            ],
            false)));
    }

    public async Task InitializeAsync(CommitDiffNavigationArgs arguments)
    {
        IsRevisionDiffMode = true;
        SetCherryPickScope(CommitRangeCherryPickScope.None);
        Title = arguments.Title;
        Description = arguments.Description;
        EmptyMessage = _localizationService.GetString("NoBranchFileChanges");
        await LoadCommitsAsync((_, _, _) => Task.FromResult(new GitCommitPage(
            [
                arguments.Commit ?? throw new InvalidDataException("CommitDiffNavigationArgs.Commit cannot be null")
            ],
            false)));
    }

    public void ShowInvalidNavigationError()
    {
        Title = _localizationService.GetString("CommitRangePageTitle");
        Description = "";
        EmptyMessage = _localizationService.GetString("CommitRangeNavigationError");
        _loadCommitPageAsync = null;
        _nextCommitOffset = 0;
        HasMoreCommits = false;
        HasNoCommits = true;
        ShowError(EmptyMessage);
    }

    protected override bool IsCommitDetailsOperationRunning => IsLoading || IsLoadingMore;

    protected override void ShowCommitDetailsError(string message, string? details = null)
    {
        ShowError(message, details);
    }

    private void ConfigureHeader(CommitRangeNavigationArgs arguments)
    {
        string branchName = arguments.Branch.Name;
        string remoteName = arguments.Remote.Name;

        if (arguments.Direction == CommitRangeDirection.Incoming)
        {
            Title = string.Format(
                _localizationService.GetString("IncomingCommitRangeTitle"),
                branchName);
            Description = string.Format(
                _localizationService.GetString("IncomingCommitRangeDescription"),
                arguments.Branch.RemoteTrackingBranch,
                branchName);
            EmptyMessage = _localizationService.GetString("NoIncomingCommits");
            return;
        }

        Title = string.Format(
            _localizationService.GetString("OutgoingCommitRangeTitle"),
            branchName);
        Description = arguments.Branch.IsPublishedToPushRemote
            ? string.Format(
                _localizationService.GetString("OutgoingCommitRangeDescription"),
                branchName,
                arguments.Branch.PushTrackingBranch)
            : string.Format(
                _localizationService.GetString("UnpublishedCommitRangeDescription"),
                branchName,
                remoteName);
        EmptyMessage = _localizationService.GetString("NoOutgoingCommits");
    }

    private async Task LoadCommitsAsync(
        System.Func<RepositoryInfo, int, int, Task<GitCommitPage>> loadCommitPageAsync)
    {
        _loadCommitPageAsync = loadCommitPageAsync;
        _nextCommitOffset = 0;
        ClearNotification();
        SelectedCommit = null;
        ClearCommits();
        HasNoCommits = false;
        HasMoreCommits = false;
        ProgressMessage = _localizationService.GetString("LoadingCommitRange");
        IsLoading = true;

        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        if (repository is null)
        {
            IsLoading = false;
            HasNoCommits = true;
            ShowError(_localizationService.GetString("OpenRepositoryBeforeHistory"));
            return;
        }

        try
        {
            GitCommitPage loadedPage = new([], false);
            await _gitService.ExecuteAsync(async () =>
            {
                loadedPage = await loadCommitPageAsync(repository, 0, CommitPageSize);
            });

            ReplaceCommitPage(loadedPage);
            await RefreshCherryPickAvailabilityAsync(repository);
            HasNoCommits = !HasUnfilteredCommits;
            OnPropertyChanged(nameof(ShowContent));
        }
        catch (FileNotFoundException)
        {
            ShowLoadError(_localizationService.GetString("GitExecutableNotFound"));
        }
        catch (DirectoryNotFoundException)
        {
            ShowLoadError(_localizationService.GetString("RepositoryFolderNotFound"));
        }
        catch (GitRemoteOperationException exception)
        {
            ShowLoadError(_localizationService.GetString("GitRemoteCommandFailed"), exception.Message);
        }
        catch (GitCommandException exception)
        {
            ShowLoadError(_localizationService.GetString("GitRemoteCommandFailed"), exception.Message);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowContent));
        }
    }

    private async Task LoadMoreCommitsAsync()
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        if (repository is null || _loadCommitPageAsync is null || !HasMoreCommits)
        {
            return;
        }

        try
        {
            ClearNotification();
            ProgressMessage = _localizationService.GetString("LoadingMoreHistoryProgress");
            IsLoadingMore = true;
            GitCommitPage loadedPage = new([], false);
            await _gitService.ExecuteAsync(async () =>
            {
                loadedPage = await _loadCommitPageAsync(
                    repository,
                    _nextCommitOffset,
                    CommitPageSize);
            });

            _nextCommitOffset += loadedPage.Commits.Count;
            AppendCommits(loadedPage.Commits);
            HasMoreCommits = loadedPage.HasMore;
        }
        catch (FileNotFoundException)
        {
            ShowError(_localizationService.GetString("GitExecutableNotFound"));
        }
        catch (DirectoryNotFoundException)
        {
            ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
        }
        catch (GitRemoteOperationException exception)
        {
            ShowError(_localizationService.GetString("GitRemoteCommandFailed"), exception.Message);
        }
        catch (GitCommandException exception)
        {
            ShowError(_localizationService.GetString("GitRemoteCommandFailed"), exception.Message);
        }
        finally
        {
            ProgressMessage = "";
            IsLoadingMore = false;
        }
    }

    private void ReplaceCommitPage(GitCommitPage page)
    {
        ReplaceCommits(page.Commits);
        _nextCommitOffset = page.Commits.Count;
        HasMoreCommits = page.HasMore;
    }

    private void ShowError(string message)
    {
        ShowNotification(AppNotificationSeverity.Error, message);
    }

    private void ShowError(string message, string? details)
    {
        ShowNotification(AppNotificationSeverity.Error, message, details);
    }

    private void ShowLoadError(string message)
    {
        EmptyMessage = message;
        HasNoCommits = true;
        ShowError(message);
    }

    private void ShowLoadError(string message, string? details)
    {
        EmptyMessage = message;
        HasNoCommits = true;
        ShowError(message, details);
    }

    protected override void OnCommitFilterChanged()
    {
        OnPropertyChanged(nameof(ShowContent));
    }

    protected override void OnCommitSelectionChanged()
    {
        OnPropertyChanged(nameof(CherryPickButtonText));
        UpdateCherryPickCommandStates();
    }

    private void SetCherryPickScope(CommitRangeCherryPickScope scope)
    {
        _cherryPickScope = scope;
        OnPropertyChanged(nameof(ShowCherryPickCommands));
        UpdateCherryPickCommandStates();
    }

    private async Task RefreshCherryPickAvailabilityAsync(RepositoryInfo repository)
    {
        if (!ShowCherryPickCommands)
        {
            _hasCleanWorkingTree = false;
            _hasOperationInProgress = false;
            UpdateCherryPickCommandStates();
            return;
        }

        GitStatusSnapshot status = await _gitService.GetStatusAsync(repository);
        GitOperationState operationState = await _gitService.GetOperationStateAsync(repository);
        _hasCleanWorkingTree = status.StagedChanges.Count == 0
            && status.UnstagedChanges.Count == 0
            && status.ConflictedChanges.Count == 0;
        _hasOperationInProgress = operationState.Kind != GitOperationKind.None;
        UpdateCherryPickCommandStates();
    }

    private async Task CherryPickSelectedCoreAsync(GitCherryPickOptions options)
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        if (repository is null)
        {
            return;
        }

        await RefreshCherryPickAvailabilityAsync(repository);
        if (!CanCherryPickSelected())
        {
            ShowError(_localizationService.GetString("CherryPickUnavailable"));
            return;
        }

        IReadOnlyList<GitCommit> commits = GitCommitSelection.OrderOldestFirst(
            AllCommits,
            SelectedCommits);
        if (commits.Count == 1 && commits[0].IsMerge)
        {
            int? mainlineParentNumber = await _dialogService.ShowCherryPickMainlineDialogAsync(commits[0]);
            if (mainlineParentNumber is null)
            {
                return;
            }

            options = options with { MainlineParentNumber = mainlineParentNumber };
        }

        bool confirmed = await _dialogService.ConfirmCherryPickAsync(
            repository.CurrentBranch,
            commits,
            options);
        if (!confirmed)
        {
            return;
        }

        await _gitService.ExecuteAsync(async () =>
        {
            IsLoading = true;
            ProgressMessage = _localizationService.GetString("CherryPickProgressMessage");
            try
            {
                ClearNotification();
                await _gitService.Commits.CherryPickAsync(repository, commits, options);
                if (options.NoCommit)
                {
                    _mainWindowViewModel.RequestChangesNavigation(
                        _localizationService.GetString("CherryPickWithoutCommitSucceeded"));
                    return;
                }

                if (_loadCommitPageAsync is not null)
                {
                    GitCommitPage loadedPage = await _loadCommitPageAsync(
                        repository,
                        0,
                        CommitPageSize);
                    ReplaceCommitPage(loadedPage);
                    HasNoCommits = !HasUnfilteredCommits;
                    OnPropertyChanged(nameof(ShowContent));
                }

                ShowNotification(
                    AppNotificationSeverity.Success,
                    commits.Count == 1
                        ? string.Format(
                            _localizationService.GetString("CherryPickSingleSucceeded"),
                            commits[0].ShortHash,
                            repository.CurrentBranch)
                        : string.Format(
                            _localizationService.GetString("CherryPickMultipleSucceeded"),
                            commits.Count,
                            repository.CurrentBranch));
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
                if (await _mainWindowViewModel.TryShowConflictWarningAsync(repository, this, exception))
                {
                    return;
                }

                ShowError(_localizationService.GetString("CherryPickFailed"), exception.Message);
            }
            finally
            {
                ProgressMessage = "";
                IsLoading = false;
            }
        });
    }

    private void UpdateCherryPickCommandStates()
    {
        CherryPickSelectedCommand.NotifyCanExecuteChanged();
        CherryPickWithSourceCommand.NotifyCanExecuteChanged();
        CherryPickWithSignOffCommand.NotifyCanExecuteChanged();
        CherryPickWithoutCommitCommand.NotifyCanExecuteChanged();
    }
}
