using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;

namespace SimpleGit11.ViewModels;

public sealed partial class ChangesViewModel : AppNotificationViewModelBase
{
    private readonly IAsyncCommandExecutor _asyncCommandExecutor;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly IGitService _gitService;
    private readonly ILocalizationService _localizationService;
    private readonly IClipboardService _clipboardService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;
    private readonly ITextFileService _textFileService;
    private TextFileDocument? _editableDocument;
    private string _editingFilePath = "";
    private bool _editPreviousFullFileMode;
    private GitChangedFile? _selectedStagedChange;
    private GitChangedFile? _selectedUnstagedChange;
    private bool _suppressSelectionLoad;

    public ChangesViewModel(
        MainWindowViewModel mainWindowViewModel,
        IGitService gitService,
        ILocalizationService localizationService,
        IClipboardService clipboardService,
        IDialogService dialogService,
        ISettingsService settingsService,
        ITextFileService textFileService,
        ConflictEditorViewModel conflictEditor,
        IMessenger messenger,
        IAsyncCommandExecutor asyncCommandExecutor)
        : base(messenger)
    {
        _mainWindowViewModel = mainWindowViewModel;
        _gitService = gitService;
        _localizationService = localizationService;
        _clipboardService = clipboardService;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _textFileService = textFileService;
        _asyncCommandExecutor = asyncCommandExecutor
            ?? throw new System.ArgumentNullException(nameof(asyncCommandExecutor));
        ConflictEditor = conflictEditor;
        ConflictEditor.ConflictResolvedAsync = OnConflictResolvedAsync;
        DiffEmptyMessage = _localizationService.GetString("SelectFileToViewDiff");
        DiffText = "";
        HasNoChanges = true;
        HasDiffEmptyState = true;
        SelectedDiffStat = DiffStat.Empty;
        SelectedChanges = [];
        InitializeSyntaxHighlightingOptions();
    }

    private bool CanRunWhenIdle() => !IsGitOperationRunning && !IsEditingFile;

    private bool CanStageAll() => UnstagedChanges.Count > 0 && CanRunWhenIdle();

    private bool CanUnstageAll() => StagedChanges.Count > 0 && CanRunWhenIdle();

    private bool CanOpenAmendDialog() => HasStagedChanges
        && !IsMergeInProgress
        && !HasSequencerOperation
        && CanRunWhenIdle();

    private bool CanAbortMerge() => IsMergeInProgress && CanRunWhenIdle();

    private bool CanRunSequencerAction() => HasSequencerOperation && CanRunWhenIdle();

    private bool CanDiscardAllUnstaged() => UnstagedChanges.Count > 0 && CanRunWhenIdle();

    private bool CanCleanUntracked() =>
        UnstagedChanges.Any(change => change.Status == "Untracked") && CanRunWhenIdle();

    private bool CanCreateStash() =>
        (StagedChanges.Count > 0 || UnstagedChanges.Count > 0) && CanRunWhenIdle();

    private bool CanRunSelectedStashOperation() => SelectedStash is not null && CanRunWhenIdle();

    private bool CanDropAllStashes() => HasStashes && CanRunWhenIdle();

    private bool CanShowSelectedChange() => SelectedChange is not null && CanRunWhenIdle();

    private bool CanRevertDiffLine() => CanRevertSelectedChange && CanRunWhenIdle();

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnRefreshStatusAsync() => _asyncCommandExecutor.ExecuteAsync(RefreshStatusAsync);

    [RelayCommand(CanExecute = nameof(CanStageSelected), FlowExceptionsToTaskScheduler = true)]
    private Task OnStageSelectedAsync() => _asyncCommandExecutor.ExecuteAsync(StageSelectedAsync);

    [RelayCommand(CanExecute = nameof(CanUnstageSelected), FlowExceptionsToTaskScheduler = true)]
    private Task OnUnstageSelectedAsync() => _asyncCommandExecutor.ExecuteAsync(UnstageSelectedAsync);

    [RelayCommand(CanExecute = nameof(CanStageAll), FlowExceptionsToTaskScheduler = true)]
    private Task OnStageAllAsync() => _asyncCommandExecutor.ExecuteAsync(StageAllAsync);

    [RelayCommand(CanExecute = nameof(CanUnstageAll), FlowExceptionsToTaskScheduler = true)]
    private Task OnUnstageAllAsync() => _asyncCommandExecutor.ExecuteAsync(UnstageAllAsync);

    [RelayCommand(CanExecute = nameof(CanOpenCommitDialog), FlowExceptionsToTaskScheduler = true)]
    private Task OnOpenCommitDialogAsync() =>
        _asyncCommandExecutor.ExecuteAsync(() => OpenCommitDialogAsync(isAmend: false));

    [RelayCommand(CanExecute = nameof(CanOpenAmendDialog), FlowExceptionsToTaskScheduler = true)]
    private Task OnOpenAmendDialogAsync() =>
        _asyncCommandExecutor.ExecuteAsync(() => OpenCommitDialogAsync(isAmend: true));

    [RelayCommand(CanExecute = nameof(CanAbortMerge), FlowExceptionsToTaskScheduler = true)]
    private Task OnAbortMergeAsync() => _asyncCommandExecutor.ExecuteAsync(AbortMergeAsync);

    [RelayCommand(CanExecute = nameof(CanRunSequencerAction), FlowExceptionsToTaskScheduler = true)]
    private Task OnContinueOperationAsync() =>
        _asyncCommandExecutor.ExecuteAsync(ContinueOperationAsync);

    [RelayCommand(CanExecute = nameof(CanRunSequencerAction), FlowExceptionsToTaskScheduler = true)]
    private Task OnSkipOperationAsync() => _asyncCommandExecutor.ExecuteAsync(SkipOperationAsync);

    [RelayCommand(CanExecute = nameof(CanRunSequencerAction), FlowExceptionsToTaskScheduler = true)]
    private Task OnAbortOperationAsync() => _asyncCommandExecutor.ExecuteAsync(AbortOperationAsync);

    [RelayCommand(CanExecute = nameof(CanDiscardSelected), FlowExceptionsToTaskScheduler = true)]
    private Task OnDiscardSelectedAsync() => _asyncCommandExecutor.ExecuteAsync(DiscardSelectedAsync);

    [RelayCommand(CanExecute = nameof(CanDiscardAllUnstaged), FlowExceptionsToTaskScheduler = true)]
    private Task OnDiscardAllUnstagedAsync() =>
        _asyncCommandExecutor.ExecuteAsync(DiscardAllUnstagedAsync);

    [RelayCommand(CanExecute = nameof(CanCleanUntracked), FlowExceptionsToTaskScheduler = true)]
    private Task OnCleanUntrackedAsync() => _asyncCommandExecutor.ExecuteAsync(CleanUntrackedAsync);

    [RelayCommand(CanExecute = nameof(CanCreateStash), FlowExceptionsToTaskScheduler = true)]
    private Task OnCreateStashAsync() => _asyncCommandExecutor.ExecuteAsync(CreateStashAsync);

    [RelayCommand(CanExecute = nameof(CanRunSelectedStashOperation), FlowExceptionsToTaskScheduler = true)]
    private Task OnApplyStashAsync() => _asyncCommandExecutor.ExecuteAsync(ApplyStashAsync);

    [RelayCommand(CanExecute = nameof(CanRunSelectedStashOperation), FlowExceptionsToTaskScheduler = true)]
    private Task OnPopStashAsync() => _asyncCommandExecutor.ExecuteAsync(PopStashAsync);

    [RelayCommand(CanExecute = nameof(CanRunSelectedStashOperation), FlowExceptionsToTaskScheduler = true)]
    private Task OnDropStashAsync() => _asyncCommandExecutor.ExecuteAsync(DropStashAsync);

    [RelayCommand(CanExecute = nameof(CanDropAllStashes), FlowExceptionsToTaskScheduler = true)]
    private Task OnDropAllStashesAsync() => _asyncCommandExecutor.ExecuteAsync(DropAllStashesAsync);

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnStageChangeAsync(GitChangedFile? change) =>
        _asyncCommandExecutor.ExecuteAsync(() => StageChangeAsync(change));

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnUnstageChangeAsync(GitChangedFile? change) =>
        _asyncCommandExecutor.ExecuteAsync(() => UnstageChangeAsync(change));

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnDiscardChangeAsync(GitChangedFile? change) =>
        _asyncCommandExecutor.ExecuteAsync(() => DiscardChangeAsync(change));

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnToggleChangeDisplayModeAsync(GitChangedFile? change) =>
        _asyncCommandExecutor.ExecuteAsync(() => ToggleChangeDisplayModeAsync(change));

    [RelayCommand(CanExecute = nameof(CanShowSelectedChange), FlowExceptionsToTaskScheduler = true)]
    private Task OnShowFullFileAsync() => _asyncCommandExecutor.ExecuteAsync(ShowFullFileAsync);

    [RelayCommand(CanExecute = nameof(CanShowSelectedChange), FlowExceptionsToTaskScheduler = true)]
    private Task OnShowDiffAsync() => _asyncCommandExecutor.ExecuteAsync(ShowDiffAsync);

    [RelayCommand(CanExecute = nameof(CanShowSelectedChange), FlowExceptionsToTaskScheduler = true)]
    private Task OnToggleFullFileAsync() => _asyncCommandExecutor.ExecuteAsync(ToggleFullFileAsync);

    [RelayCommand(CanExecute = nameof(CanRevertDiffLine), FlowExceptionsToTaskScheduler = true)]
    private Task OnRevertDiffLineAsync(DiffLine? line) =>
        _asyncCommandExecutor.ExecuteAsync(() => RevertDiffLineAsync(line));

    [RelayCommand(CanExecute = nameof(CanEditSelectedFile), FlowExceptionsToTaskScheduler = true)]
    private Task OnEditSelectedFileAsync() => _asyncCommandExecutor.ExecuteAsync(EditSelectedFileAsync);

    [RelayCommand(CanExecute = nameof(CanSaveEditedFile), FlowExceptionsToTaskScheduler = true)]
    private Task OnSaveEditedFileAsync(string? text) =>
        _asyncCommandExecutor.ExecuteAsync(() => SaveEditedFileAsync(text));

    [RelayCommand(FlowExceptionsToTaskScheduler = true)]
    private Task OnCancelEditedFileAsync() =>
        _asyncCommandExecutor.ExecuteAsync(CancelEditedFileAsync);

    [RelayCommand]
    private void OnMarkEditedFileChanged()
    {
        if (IsEditingFile)
        {
            IsEditedFileDirty = true;
        }
    }

    [RelayCommand]
    private void OnCopyText(string? text)
    {
        if (text is not null)
        {
            _clipboardService.SetText(text);
        }
    }

    public ConflictEditorViewModel ConflictEditor { get; }

    public ObservableCollection<GitChangedFile> StagedChanges { get; } = [];

    public ObservableCollection<GitChangedFile> UnstagedChanges { get; } = [];

    public ObservableCollection<GitChangedFile> ConflictedChanges { get; } = [];

    public ObservableCollection<GitChangedFile> AllChanges { get; } = [];

    public bool HasStagedChanges => StagedChanges.Count > 0;
    public bool HasUnstagedChanges => UnstagedChanges.Count > 0;
    public bool HasConflictedChanges => ConflictedChanges.Count > 0;
    public bool CanStageSelected =>
        SelectedChanges.Any(change => change.State == GitChangeState.Unstaged) && CanRunWhenIdle();

    public bool CanUnstageSelected =>
        SelectedChanges.Any(change => change.State == GitChangeState.Staged) && CanRunWhenIdle();

    public ObservableCollection<DiffLine> DiffLines { get; } = [];

    public ObservableCollection<DisplayOption<SyntaxHighlightingMode>> SyntaxHighlightingOptions { get; } = [];

    public ObservableCollection<GitStash> Stashes { get; } = [];

    public bool CanOpenCommitDialog => _mainWindowViewModel.CurrentRepository is not null
        && (HasStagedChanges || !HasConflictedChanges)
        && !HasSequencerOperation
        && CanRunWhenIdle();

    public bool CanDiscardSelected => SelectedChanges.Count > 0
        && SelectedChanges.All(change => change.CanDiscard)
        && CanRunWhenIdle();

    public DiffStat ChangedFilesStat => DiffStat.Sum(AllChanges.Select(change => change.Stat));

    public string ChangedFilesTitle => PluralizationService.FormatChangeCount(
        AllChanges.Select(change => change.Path).Distinct(System.StringComparer.OrdinalIgnoreCase).Count(),
        _localizationService);

    [ObservableProperty]
    public partial DiffStat SelectedDiffStat { get; private set; }

    public string SelectedDiffFileTitle => SelectedChange?.FileName
        ?? _localizationService.GetString("NoFileSelected");

    public string SelectedDiffFileTooltip => SelectedChange?.Path ?? "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStashes))]
    [NotifyCanExecuteChangedFor(nameof(DropAllStashesCommand))]
    public partial int StashCount { get; private set; }

    public bool HasStashes => StashCount > 0;

    [ObservableProperty]
    public partial bool IsRefreshing { get; private set; }

    [ObservableProperty]
    public partial bool IsDiffLoading { get; private set; }

    partial void OnIsDiffLoadingChanged(bool value)
    {
        SaveEditedFileCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    public partial bool IsGitOperationRunning { get; private set; }

    partial void OnIsGitOperationRunningChanged(bool value)
    {
        UpdateCommandStates();
        PublishOperationState(
            value,
            _localizationService.GetString("OperationInProgressMessage"));
    }

    public bool IsRepositoryOpen => _mainWindowViewModel.CurrentRepository is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoChangesNotice))]
    [NotifyPropertyChangedFor(nameof(ShowStatusNotices))]
    public partial bool HasNoChanges { get; private set; }

    public bool HasChanges => AllChanges.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMergeInProgress))]
    [NotifyPropertyChangedFor(nameof(IsRebaseInProgress))]
    [NotifyPropertyChangedFor(nameof(IsCherryPickInProgress))]
    [NotifyPropertyChangedFor(nameof(IsRevertInProgress))]
    [NotifyPropertyChangedFor(nameof(HasOperationInProgress))]
    [NotifyPropertyChangedFor(nameof(HasSequencerOperation))]
    [NotifyPropertyChangedFor(nameof(HasPendingBranchOperation))]
    [NotifyPropertyChangedFor(nameof(ShowDiscardCommands))]
    [NotifyPropertyChangedFor(nameof(ShowNoChangesNotice))]
    [NotifyPropertyChangedFor(nameof(ShowStatusNotices))]
    [NotifyPropertyChangedFor(nameof(ShowMergeNotice))]
    [NotifyPropertyChangedFor(nameof(ShowRebaseNotice))]
    [NotifyPropertyChangedFor(nameof(ShowCherryPickNotice))]
    [NotifyPropertyChangedFor(nameof(ShowRevertNotice))]
    public partial GitOperationState OperationState { get; private set; } = GitOperationState.None;

    public bool IsMergeInProgress => OperationState.Kind == GitOperationKind.Merge;

    public bool IsRebaseInProgress => OperationState.Kind == GitOperationKind.Rebase;

    public bool IsCherryPickInProgress => OperationState.Kind == GitOperationKind.CherryPick;

    public bool IsRevertInProgress => OperationState.Kind == GitOperationKind.Revert;

    public bool HasOperationInProgress => OperationState.Kind != GitOperationKind.None;

    public bool HasSequencerOperation => OperationState.Kind is
        GitOperationKind.Rebase or GitOperationKind.CherryPick or GitOperationKind.Revert;

    public bool HasPendingBranchOperation => IsMergeInProgress || IsRebaseInProgress;

    public bool ShowDiscardCommands => !HasOperationInProgress;

    public bool ShowNoChangesNotice => HasNoChanges && !HasOperationInProgress;

    public bool ShowStatusNotices => ShowNoChangesNotice || HasOperationInProgress;

    public bool ShowMergeNotice => IsMergeInProgress;

    public bool ShowRebaseNotice => IsRebaseInProgress;

    public bool ShowCherryPickNotice => IsCherryPickInProgress;

    public bool ShowRevertNotice => IsRevertInProgress;

    partial void OnOperationStateChanged(GitOperationState value)
    {
        UpdateCommandStates();
    }

    [ObservableProperty]
    public partial bool HasDiffEmptyState { get; private set; }

    [ObservableProperty]
    public partial string DiffEmptyMessage { get; private set; }

    [ObservableProperty]
    public partial string DiffText { get; private set; }

    [ObservableProperty]
    public partial string EditableFileText { get; private set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<DiffLine> EditableDiffLines { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiffInteractionEnabled))]
    [NotifyPropertyChangedFor(nameof(CanEditSelectedFile))]
    public partial bool IsEditingFile { get; private set; }

    partial void OnIsEditingFileChanged(bool value)
    {
        SaveEditedFileCommand.NotifyCanExecuteChanged();
        UpdateCommandStates();
    }

    [ObservableProperty]
    public partial bool IsEditedFileDirty { get; private set; }

    partial void OnIsEditedFileDirtyChanged(bool value)
    {
        SaveEditedFileCommand.NotifyCanExecuteChanged();
    }

    public bool IsDiffInteractionEnabled => !IsEditingFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiffModeToggleText))]
    [NotifyPropertyChangedFor(nameof(ContextMenuDisplayModeText))]
    public partial bool IsFullFileMode { get; private set; }

    partial void OnIsFullFileModeChanged(bool value)
    {
        UpdateCommandStates();
    }

    public string DiffModeToggleText => IsFullFileMode
        ? _localizationService.GetString("ShowDiffMenuFlyoutItemText")
        : _localizationService.GetString("ShowFullFileMenuFlyoutItemText");

    public string ContextMenuDisplayModeText => DiffModeToggleText;

    public bool IgnoreWhitespaceInDiff
    {
        get => _settingsService.Current.IgnoreWhitespaceInDiff;
        set
        {
            if (value == _settingsService.Current.IgnoreWhitespaceInDiff)
            {
                return;
            }

            _settingsService.SetIgnoreWhitespaceInDiff(value);
            OnPropertyChanged();
            if (SelectedChange is not null)
            {
                _ = LoadSelectedChangeAsync(SelectedChange);
            }
        }
    }

    [ObservableProperty]
    public partial DisplayOption<SyntaxHighlightingMode>? SelectedSyntaxHighlightingOption { get; set; }

    partial void OnSelectedSyntaxHighlightingOptionChanged(DisplayOption<SyntaxHighlightingMode>? value)
    {
        OnPropertyChanged(nameof(SelectedSyntaxHighlightingMode));
    }

    public SyntaxHighlightingMode SelectedSyntaxHighlightingMode =>
        SelectedSyntaxHighlightingOption?.Value ?? SyntaxHighlightingMode.Auto;

    public bool CanRevertSelectedChange => SelectedUnstagedChange is not null
        && !SelectedUnstagedChange.IsConflicted
        && SelectedUnstagedChange.Status != "Untracked"
        && CanRunWhenIdle();

    public bool CanEditSelectedFile => IsRepositoryOpen
        && SelectedChange is not null
        && !SelectedChange.IsConflicted
        && SelectedChange.Status != "Deleted"
        && CanRunWhenIdle();

    private bool CanSaveEditedFile() =>
        IsEditingFile && IsEditedFileDirty && !IsDiffLoading && _editableDocument is not null;

    public Visibility ConflictEditorVisibility => SelectedChange?.IsConflicted == true
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility DiffViewerVisibility => SelectedChange?.IsConflicted == true
        ? Visibility.Collapsed
        : Visibility.Visible;

    [ObservableProperty]
    public partial bool HasLocalCommits { get; private set; }

    public GitChangedFile? SelectedStagedChange
    {
        get => _selectedStagedChange;
        set
        {
            if (SetProperty(ref _selectedStagedChange, value) && value is not null)
            {
                SelectedUnstagedChange = null;
                OnPropertyChanged(nameof(SelectedChange));
                OnDiffSelectionChanged();
                OnPropertyChanged(nameof(CanRevertSelectedChange));
                UpdateCommandStates();
                if (!_suppressSelectionLoad)
                {
                    _ = LoadSelectedChangeAsync(value);
                }
            }
            else
            {
                OnPropertyChanged(nameof(SelectedChange));
                OnDiffSelectionChanged();
                OnPropertyChanged(nameof(CanRevertSelectedChange));
                UpdateCommandStates();
            }
        }
    }

    public GitChangedFile? SelectedChange => SelectedUnstagedChange ?? SelectedStagedChange;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStageSelected))]
    [NotifyPropertyChangedFor(nameof(CanUnstageSelected))]
    public partial IReadOnlyList<GitChangedFile> SelectedChanges { get; private set; }

    partial void OnSelectedChangesChanged(IReadOnlyList<GitChangedFile> value)
    {
        UpdateCommandStates();
    }

    public GitChangedFile? SelectedUnstagedChange
    {
        get => _selectedUnstagedChange;
        set
        {
            if (SetProperty(ref _selectedUnstagedChange, value) && value is not null)
            {
                SelectedStagedChange = null;
                OnPropertyChanged(nameof(SelectedChange));
                OnDiffSelectionChanged();
                OnPropertyChanged(nameof(CanRevertSelectedChange));
                UpdateCommandStates();
                if (!_suppressSelectionLoad)
                {
                    _ = LoadSelectedChangeAsync(value);
                }
            }
            else
            {
                OnPropertyChanged(nameof(SelectedChange));
                OnDiffSelectionChanged();
                OnPropertyChanged(nameof(CanRevertSelectedChange));
                UpdateCommandStates();
            }
        }
    }

    [ObservableProperty]
    public partial GitStash? SelectedStash { get; set; }

    partial void OnSelectedStashChanged(GitStash? value)
    {
        UpdateCommandStates();
    }

    public void SetSelectedChanges(IEnumerable<GitChangedFile> selectedChanges, GitChangedFile? lastSelectedChange)
    {
        SelectedChanges = selectedChanges.ToList();

        if (lastSelectedChange is null)
        {
            ClearDiff(_localizationService.GetString("SelectFileToViewDiff"));
            return;
        }

        if (lastSelectedChange.State == GitChangeState.Staged)
        {
            SelectedStagedChange = lastSelectedChange;
        }
        else
        {
            SelectedUnstagedChange = lastSelectedChange;
        }
    }

    public async Task RefreshStatusAsync()
    {
        await RunGitOperationAsync(async () =>
        {
            await RefreshStatusCoreAsync();
        });
    }

    private async Task RefreshStatusCoreAsync(bool clearResultMessages = true)
    {
        if (clearResultMessages)
        {
            ClearResultMessages();
        }

        ClearDiff(_localizationService.GetString("SelectFileToViewDiff"));

        if (_mainWindowViewModel.CurrentRepository is null)
        {
            ClearChanges();
            ShowError(_localizationService.GetString("OpenRepositoryBeforeStatus"));
            return;
        }

        try
        {
            IsRefreshing = true;
            HasNoChanges = false;

            Task<bool> hasLocalCommitsTask =
                _gitService.History.HasLocalCommits(_mainWindowViewModel.CurrentRepository);
            Task<GitStatusSnapshot> statusTask =
                _gitService.GetStatusAsync(_mainWindowViewModel.CurrentRepository);
            Task<GitOperationState> operationStateTask =
                _gitService.GetOperationStateAsync(_mainWindowViewModel.CurrentRepository);
            await Task.WhenAll(hasLocalCommitsTask, statusTask, operationStateTask);

            HasLocalCommits = await hasLocalCommitsTask;
            GitStatusSnapshot snapshot = await statusTask;
            OperationState = await operationStateTask;
            ReplaceChanges(StagedChanges, snapshot.StagedChanges);
            ReplaceChanges(UnstagedChanges, snapshot.UnstagedChanges);
            ReplaceChanges(ConflictedChanges, snapshot.ConflictedChanges);
            ReplaceAllChanges();
            await RefreshStashesCoreAsync();
            OnPropertyChanged(nameof(HasStagedChanges));
            OnPropertyChanged(nameof(HasUnstagedChanges));
            OnPropertyChanged(nameof(HasConflictedChanges));
            OnPropertyChanged(nameof(CanOpenCommitDialog));
            OnPropertyChanged(nameof(SelectedChange));
            HasNoChanges = !(HasStagedChanges || HasUnstagedChanges || HasConflictedChanges);
            if (_mainWindowViewModel.TryConsumeChangesNotice(out string message, out string? details))
            {
                ShowNotification(AppNotificationSeverity.Warning, message, details);
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
        finally
        {
            IsRefreshing = false;
            UpdateCommandStates();
        }
    }

    private void ClearChanges()
    {
        StagedChanges.Clear();
        UnstagedChanges.Clear();
        ConflictedChanges.Clear();
        AllChanges.Clear();
        SelectedChanges = [];
        Stashes.Clear();
        SelectedStash = null;
        StashCount = 0;
        OperationState = GitOperationState.None;
        HasNoChanges = false;
        OnPropertyChanged(nameof(HasStagedChanges));
        OnPropertyChanged(nameof(HasUnstagedChanges));
        OnPropertyChanged(nameof(HasConflictedChanges));
        OnPropertyChanged(nameof(CanOpenCommitDialog));
        OnPropertyChanged(nameof(SelectedChange));
        OnChangedFilesSummaryChanged();
    }

    private async Task LoadDiffAsync(GitChangedFile statusEntry)
    {
        await RunQueuedReadOperationAsync(async () =>
        {
            if (!ReferenceEquals(SelectedChange, statusEntry))
            {
                return;
            }

            await LoadDiffCoreAsync(statusEntry);
        });
    }

    private async Task LoadSelectedChangeAsync(GitChangedFile statusEntry)
    {
        if (statusEntry.IsConflicted)
        {
            if (_mainWindowViewModel.CurrentRepository is RepositoryInfo repository)
            {
                DiffLines.Clear();
                HasDiffEmptyState = false;
                await ConflictEditor.LoadAsync(repository, statusEntry);
            }

            return;
        }

        ConflictEditor.Clear();
        if (IsFullFileMode)
        {
            await ShowFullFileAsync(statusEntry);
        }
        else
        {
            await LoadDiffAsync(statusEntry);
        }
    }

    private async Task LoadDiffCoreAsync(GitChangedFile statusEntry)
    {
        ClearError();

        if (_mainWindowViewModel.CurrentRepository is null)
        {
            ClearDiff(_localizationService.GetString("OpenRepositoryBeforeStatus"));
            return;
        }

        try
        {
            IsDiffLoading = true;
            var diff = await _gitService.Diff.GetDiffAsync(_mainWindowViewModel.CurrentRepository, statusEntry);
            DiffLines.Clear();

            foreach (var line in diff.Lines)
            {
                DiffLines.Add(line);
            }

            SelectedDiffStat = diff.Stat;
            DiffText = DiffTextFormatter.FormatText(diff.Lines);
            HasDiffEmptyState = diff.IsEmpty;
            DiffEmptyMessage = diff.IsEmpty ? LocalizeDiffEmptyMessage(diff.EmptyMessage) : "";
        }
        catch (FileNotFoundException)
        {
            ShowError(_localizationService.GetString("GitExecutableNotFound"));
        }
        catch (GitCommandException exception)
        {
            ShowError(_localizationService.GetString("GitDiffCommandFailed"), exception.Message);
        }
        finally
        {
            IsDiffLoading = false;
        }
    }

    private Task ShowFullFileAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedChange is null)
        {
            return Task.CompletedTask;
        }

        return ShowFullFileAsync(SelectedChange);
    }

    private async Task ShowFullFileAsync(GitChangedFile change)
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            return;
        }

        IsFullFileMode = true;
        var repository = _mainWindowViewModel.CurrentRepository;
        await RunQueuedReadOperationAsync(async () =>
        {
            if (!ReferenceEquals(SelectedChange, change))
            {
                return;
            }

            try
            {
                ClearError();
                IsDiffLoading = true;
                Task<DiffResult> diffTask = _gitService.Diff.GetDiffAsync(repository, change);
                Task<string> textTask = _gitService.Diff.GetFullFileTextAsync(repository, change);
                await Task.WhenAll(diffTask, textTask);

                DiffResult diff = await diffTask;
                string text = await textTask;
                var fullFileLines = DiffTextFormatter.FormatFullFile(text, diff.Lines, false);
                DiffLines.Clear();
                foreach (var line in fullFileLines)
                {
                    DiffLines.Add(line);
                }

                SelectedDiffStat = diff.Stat;
                DiffText = DiffTextFormatter.FormatText(fullFileLines);
                HasDiffEmptyState = string.IsNullOrWhiteSpace(text);
                DiffEmptyMessage = HasDiffEmptyState
                    ? _localizationService.GetString("NoTextDiffAvailable")
                    : "";
            }
            catch (FileNotFoundException)
            {
                ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
            }
            catch (GitCommandException exception)
            {
                ShowError(_localizationService.GetString("GitDiffCommandFailed"), exception.Message);
            }
            finally
            {
                IsDiffLoading = false;
            }
        });
    }

    private async Task ShowDiffAsync()
    {
        if (SelectedChange is not null)
        {
            IsFullFileMode = false;
            await LoadDiffAsync(SelectedChange);
        }
    }

    private async Task ToggleFullFileAsync()
    {
        if (IsFullFileMode)
        {
            await ShowFullFileAsync();
        }
        else
        {
            await ShowDiffAsync();
        }
    }

    private async Task EditSelectedFileAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null ||
            SelectedChange is null ||
            !CanEditSelectedFile)
        {
            return;
        }

        RepositoryInfo repository = _mainWindowViewModel.CurrentRepository;
        GitChangedFile change = SelectedChange;
        try
        {
            ClearError();
            IsDiffLoading = true;
            Task<TextFileDocument> documentTask = _textFileService.ReadAsync(repository, change.Path);
            Task<DiffResult> diffTask = _gitService.Diff.GetDiffAsync(repository, change);
            await Task.WhenAll(documentTask, diffTask);

            if (!ReferenceEquals(SelectedChange, change))
            {
                return;
            }

            _editableDocument = await documentTask;
            _editingFilePath = change.Path;
            _editPreviousFullFileMode = IsFullFileMode;
            EditableFileText = _editableDocument.Text;
            EditableDiffLines = DiffTextFormatter.FormatEditableFile(
                _editableDocument.Text,
                (await diffTask).Lines);
            IsEditedFileDirty = false;
            IsEditingFile = true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or DecoderFallbackException)
        {
            ShowError(_localizationService.GetString("FileEditorOpenFailed"), exception.Message);
        }
        catch (GitCommandException exception)
        {
            ShowError(_localizationService.GetString("GitDiffCommandFailed"), exception.Message);
        }
        finally
        {
            IsDiffLoading = false;
        }
    }

    private async Task SaveEditedFileAsync(string? text)
    {
        if (_mainWindowViewModel.CurrentRepository is not RepositoryInfo repository ||
            _editableDocument is null ||
            text is null ||
            !CanSaveEditedFile())
        {
            return;
        }

        string editedPath = _editingFilePath;
        bool restoreFullFileMode = _editPreviousFullFileMode;
        try
        {
            ClearError();
            IsDiffLoading = true;
            await _textFileService.WriteAsync(_editableDocument, text);
            IsEditedFileDirty = false;
            EndEditingFile();

            await RefreshStatusCoreAsync(clearResultMessages: false);

            GitChangedFile? refreshedChange = UnstagedChanges
                .FirstOrDefault(item => item.Path.Equals(editedPath, StringComparison.OrdinalIgnoreCase))
                ?? StagedChanges.FirstOrDefault(
                    item => item.Path.Equals(editedPath, StringComparison.OrdinalIgnoreCase));
            if (refreshedChange is null)
            {
                return;
            }

            _suppressSelectionLoad = true;
            try
            {
                SelectSingleChange(refreshedChange);
            }
            finally
            {
                _suppressSelectionLoad = false;
            }

            IsFullFileMode = restoreFullFileMode;
            await LoadSelectedChangeAsync(refreshedChange);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or EncoderFallbackException)
        {
            ShowError(_localizationService.GetString("FileEditorSaveFailed"), exception.Message);
        }
        catch (GitCommandException exception)
        {
            ShowError(_localizationService.GetString("GitStatusCommandFailed"), exception.Message);
        }
        finally
        {
            IsDiffLoading = false;
        }
    }

    private async Task CancelEditedFileAsync()
    {
        if (!IsEditingFile)
        {
            return;
        }

        if (IsEditedFileDirty)
        {
            bool discard = await _dialogService.ConfirmAsync(
                _localizationService.GetString("FileEditorUnsavedDialogTitle"),
                string.Format(
                    _localizationService.GetString("FileEditorUnsavedDialogMessage"),
                    SelectedChange?.FileName ?? _editingFilePath),
                _localizationService.GetString("FileEditorUnsavedDialogDiscardButton"));
            if (!discard)
            {
                return;
            }
        }

        EndEditingFile();
    }

    private void EndEditingFile()
    {
        IsEditingFile = false;
        IsEditedFileDirty = false;
        EditableFileText = "";
        EditableDiffLines = [];
        _editableDocument = null;
        _editingFilePath = "";
    }

    private Task ToggleChangeDisplayModeAsync(object? parameter)
    {
        if (parameter is not GitChangedFile change || change.IsConflicted)
        {
            return Task.CompletedTask;
        }

        bool showFullFile = !IsFullFileMode;
        IsFullFileMode = showFullFile;
        if (ReferenceEquals(SelectedChange, change))
        {
            return showFullFile
                ? ShowFullFileAsync()
                : ShowDiffAsync();
        }

        SelectSingleChange(change);
        return Task.CompletedTask;
    }

    public async Task RevertChangeAsync(int lineNumber)
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedUnstagedChange is null || !CanRevertSelectedChange)
        {
            return;
        }

        var repository = _mainWindowViewModel.CurrentRepository;
        var change = SelectedUnstagedChange;
        await RunGitOperationAsync(async () =>
        {
            try
            {
                ClearResultMessages();
                await RunMutationAndRefreshStatusAsync(
                    () => _gitService.Diff.RevertChangeAsync(repository, change, lineNumber));
                _selectedUnstagedChange = UnstagedChanges.FirstOrDefault(item => item.Path == change.Path);
                _selectedStagedChange = null;
                OnPropertyChanged(nameof(SelectedUnstagedChange));
                OnPropertyChanged(nameof(SelectedStagedChange));
                OnPropertyChanged(nameof(SelectedChange));
                OnDiffSelectionChanged();
                OnPropertyChanged(nameof(CanRevertSelectedChange));
                UpdateCommandStates();
                if (SelectedUnstagedChange is not null)
                {
                    Task<DiffResult> diffTask = _gitService.Diff.GetDiffAsync(repository, SelectedUnstagedChange);
                    Task<string> textTask = _gitService.Diff.GetFullFileTextAsync(repository, SelectedUnstagedChange);
                    await Task.WhenAll(diffTask, textTask);

                    DiffResult diff = await diffTask;
                    string text = await textTask;
                    var fullFileLines = DiffTextFormatter.FormatFullFile(text, diff.Lines, false);
                    DiffLines.Clear();
                    foreach (var line in fullFileLines)
                    {
                        DiffLines.Add(line);
                    }

                    SelectedDiffStat = diff.Stat;
                    DiffText = DiffTextFormatter.FormatText(fullFileLines);
                    HasDiffEmptyState = string.IsNullOrWhiteSpace(text);
                    DiffEmptyMessage = HasDiffEmptyState
                        ? _localizationService.GetString("NoTextDiffAvailable")
                        : "";
                }

                ShowSuccess(_localizationService.GetString("RevertChangeSucceeded"));
            }
            catch (FileNotFoundException)
            {
                ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
            }
            catch (GitCommandException exception)
            {
                ShowError(_localizationService.GetString("GitDiffCommandFailed"), exception.Message);
            }
        });
    }

    private async Task StageSelectedAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeStatus"));
            return;
        }

        var changes = SelectedChanges
            .Where(change => change.State == GitChangeState.Unstaged)
            .ToList();

        if (changes.Count == 0)
        {
            return;
        }

        var repository = _mainWindowViewModel.CurrentRepository;
        var path = changes.Last().Path;
        await RunStagingOperationAsync(
            async () =>
            {
                foreach (var change in changes)
                {
                    await _gitService.Staging.StageAsync(repository, change);
                }
            },
            path,
            GitChangeState.Staged);
    }

    private async Task StageChangeAsync(object? parameter)
    {
        if (parameter is not GitChangedFile change || change.State != GitChangeState.Unstaged)
        {
            return;
        }

        SelectSingleChange(change);
        await StageSelectedAsync();
    }

    private async Task UnstageSelectedAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeStatus"));
            return;
        }

        var changes = SelectedChanges
            .Where(change => change.State == GitChangeState.Staged)
            .ToList();

        if (changes.Count == 0)
        {
            return;
        }

        var repository = _mainWindowViewModel.CurrentRepository;
        var path = changes.Last().Path;
        await RunStagingOperationAsync(
            async () =>
            {
                foreach (var change in changes)
                {
                    await _gitService.Staging.UnstageAsync(repository, change);
                }
            },
            path,
            GitChangeState.Unstaged);
    }

    private async Task UnstageChangeAsync(object? parameter)
    {
        if (parameter is not GitChangedFile change || change.State != GitChangeState.Staged)
        {
            return;
        }

        SelectSingleChange(change);
        await UnstageSelectedAsync();
    }

    private async Task StageAllAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeStatus"));
            return;
        }

        var repository = _mainWindowViewModel.CurrentRepository;
        await RunStagingOperationAsync(
            () => _gitService.Staging.StageAllAsync(repository),
            null,
            GitChangeState.Staged);
    }

    private async Task UnstageAllAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeStatus"));
            return;
        }

        var repository = _mainWindowViewModel.CurrentRepository;
        await RunStagingOperationAsync(
            () => _gitService.Staging.UnstageAllAsync(repository),
            null,
            GitChangeState.Unstaged);
    }

    private async Task DiscardSelectedAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeStatus"));
            return;
        }

        if (SelectedChanges.Any(change => !change.CanDiscard))
        {
            return;
        }

        IReadOnlyList<GitChangedFile> selectedChanges = SelectedChanges
            .GroupBy(change => change.Path, System.StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (selectedChanges.Count == 0)
        {
            return;
        }

        var repository = _mainWindowViewModel.CurrentRepository;

        if (selectedChanges.Count == 1)
        {
            GitChangedFile change = selectedChanges[0];
            bool isConfirmed = await _dialogService.ConfirmAsync(
                _localizationService.GetString("DiscardFileDialogTitle"),
                string.Format(_localizationService.GetString("DiscardFileDialogMessage"), change.Path),
                _localizationService.GetString("DiscardFileDialogPrimaryButton"));

            if (!isConfirmed)
            {
                return;
            }

            await RunDangerousOperationAsync(
                () => _gitService.ChangeRecovery.DiscardFileAsync(repository, change),
                string.Format(_localizationService.GetString("DiscardFileSucceeded"), change.Path));
            return;
        }

        var confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("DiscardSelectedFilesDialogTitle"),
            string.Format(_localizationService.GetString("DiscardSelectedFilesDialogMessage"), selectedChanges.Count),
            _localizationService.GetString("DiscardSelectedFilesDialogPrimaryButton"));

        if (!confirmed)
        {
            return;
        }

        await RunDangerousOperationAsync(
            () => _gitService.ChangeRecovery.DiscardFilesAsync(repository, selectedChanges),
            string.Format(_localizationService.GetString("DiscardSelectedFilesSucceeded"), selectedChanges.Count));
    }

    private async Task DiscardChangeAsync(object? parameter)
    {
        if (parameter is not GitChangedFile change || !change.CanDiscard)
        {
            return;
        }

        SelectSingleChange(change);
        await DiscardSelectedAsync();
    }

    private async Task DiscardAllUnstagedAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeStatus"));
            return;
        }

        var repository = _mainWindowViewModel.CurrentRepository;
        var confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("DiscardAllUnstagedDialogTitle"),
            _localizationService.GetString("DiscardAllUnstagedDialogMessage"),
            _localizationService.GetString("DiscardAllUnstagedDialogPrimaryButton"));

        if (!confirmed)
        {
            return;
        }

        await RunDangerousOperationAsync(
            () => _gitService.ChangeRecovery.DiscardUnstagedChangesAsync(repository),
            _localizationService.GetString("DiscardAllUnstagedSucceeded"));
    }

    private async Task CleanUntrackedAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeStatus"));
            return;
        }

        var repository = _mainWindowViewModel.CurrentRepository;
        var confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("CleanUntrackedDialogTitle"),
            _localizationService.GetString("CleanUntrackedDialogMessage"),
            _localizationService.GetString("CleanUntrackedDialogPrimaryButton"));

        if (!confirmed)
        {
            return;
        }

        await RunDangerousOperationAsync(
            () => _gitService.ChangeRecovery.CleanUntrackedFilesAsync(repository),
            _localizationService.GetString("CleanUntrackedSucceeded"));
    }

    private async Task CreateStashAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeStatus"));
            return;
        }

        var repository = _mainWindowViewModel.CurrentRepository;

        await RunDangerousOperationAsync(
            () => _gitService.Stashes.CreateStashAsync(repository),
            _localizationService.GetString("CreateStashSucceeded"));
    }

    private async Task ApplyStashAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedStash is null)
        {
            ShowError(_localizationService.GetString("SelectStashBeforeOperation"));
            return;
        }

        var repository = _mainWindowViewModel.CurrentRepository;
        var stash = SelectedStash;
        var confirmed = await ConfirmStashOperationAsync(
            "ApplyStashDialogTitle",
            "ApplyStashDialogMessage",
            "ApplyStashDialogPrimaryButton",
            stash);

        if (!confirmed)
        {
            return;
        }

        await RunDangerousOperationAsync(
            () => _gitService.Stashes.ApplyStashAsync(repository, stash),
            string.Format(_localizationService.GetString("ApplyStashSucceeded"), stash.Reference));
    }

    private async Task PopStashAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedStash is null)
        {
            ShowError(_localizationService.GetString("SelectStashBeforeOperation"));
            return;
        }

        var repository = _mainWindowViewModel.CurrentRepository;
        var stash = SelectedStash;
        var confirmed = await ConfirmStashOperationAsync(
            "PopStashDialogTitle",
            "PopStashDialogMessage",
            "PopStashDialogPrimaryButton",
            stash);

        if (!confirmed)
        {
            return;
        }

        await RunDangerousOperationAsync(
            () => _gitService.Stashes.PopStashAsync(repository, stash),
            string.Format(_localizationService.GetString("PopStashSucceeded"), stash.Reference));
    }

    private async Task DropStashAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedStash is null)
        {
            ShowError(_localizationService.GetString("SelectStashBeforeOperation"));
            return;
        }

        var repository = _mainWindowViewModel.CurrentRepository;
        var stash = SelectedStash;
        var confirmed = await ConfirmStashOperationAsync(
            "DropStashDialogTitle",
            "DropStashDialogMessage",
            "DropStashDialogPrimaryButton",
            stash);

        if (!confirmed)
        {
            return;
        }

        await RunDangerousOperationAsync(
            () => _gitService.Stashes.DropStashAsync(repository, stash),
            string.Format(_localizationService.GetString("DropStashSucceeded"), stash.Reference));
    }

    private async Task DropAllStashesAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null || !HasStashes)
        {
            return;
        }

        RepositoryInfo repository = _mainWindowViewModel.CurrentRepository;
        bool confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("DropAllStashesDialogTitle"),
            string.Format(_localizationService.GetString("DropAllStashesDialogMessage"), StashCount),
            _localizationService.GetString("DropAllStashesDialogPrimaryButton"));

        if (!confirmed)
        {
            return;
        }

        await RunDangerousOperationAsync(
            () => _gitService.Stashes.ClearStashesAsync(repository),
            _localizationService.GetString("DropAllStashesSucceeded"));
    }

    private async Task OpenCommitDialogAsync(bool isAmend)
    {
        IReadOnlyList<GitChangedFile> changedFiles = AllChanges.ToArray();
        CommitDialogRequest request = isAmend
            ? CommitDialogRequest.CreateAmend(changedFiles, !HasLocalCommits)
            : IsMergeInProgress
                ? CommitDialogRequest.CreateMerge(
                    changedFiles,
                    OperationState.PreparedCommitMessage)
                : CommitDialogRequest.CreateCommit(changedFiles);
        CommitDialogResult? answer = await _dialogService.ShowCommitDialogAsync(request);
        if (answer is null)
        {
            return;
        }

        await CommitAsync(answer.Message, answer.Amend);
    }

    private async Task RevertDiffLineAsync(object? parameter)
    {
        if (parameter is not DiffLine diffLine
            || diffLine.Kind is not (DiffLineKind.Added or DiffLineKind.Removed)
            || !diffLine.SourceLineNumber.HasValue)
        {
            return;
        }

        await RevertChangeAsync(diffLine.SourceLineNumber.Value);
    }

    public async Task CommitAsync(string? message, bool isAmend)
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeStatus"));
            return;
        }

        var trimmedMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        if (!isAmend && string.IsNullOrWhiteSpace(trimmedMessage))
        {
            ShowError(_localizationService.GetString("CommitRequiresMessage"));
            return;
        }

        RepositoryInfo repository = _mainWindowViewModel.CurrentRepository;
        bool isMergeCommit = IsMergeInProgress && !isAmend;

        await RunGitOperationAsync(async () =>
        {
            try
            {
                ClearResultMessages();
                GitCommitOperationResult operationResult = GitCommitOperationResult.Canceled;
                await RunMutationAndRefreshStatusAsync(async () =>
                {
                    if (isAmend)
                    {
                        operationResult = await _gitService.CommitWorkflow.AmendAsync(
                            repository,
                            trimmedMessage);
                    }
                    else if (isMergeCommit)
                    {
                        operationResult = await _gitService.CommitWorkflow.CompleteMergeAsync(
                            repository,
                            trimmedMessage!);
                    }
                    else
                    {
                        operationResult = await _gitService.CommitWorkflow.CreateAsync(
                            repository,
                            trimmedMessage!);
                    }
                });

                if (!operationResult.Completed)
                {
                    return;
                }

                string successMessage = _localizationService.GetString(
                    isAmend ? "CommitMessageAmended" : "CommitCreated");
                ShowSuccess(successMessage, operationResult.Output);
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
        });
    }

    private async Task AbortMergeAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is not RepositoryInfo repository)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeStatus"));
            return;
        }

        bool confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("AbortMergeDialogTitle"),
            _localizationService.GetString("AbortMergeDialogMessage"),
            _localizationService.GetString("AbortMergeDialogPrimaryButton"));
        if (!confirmed)
        {
            return;
        }

        await RunDangerousOperationAsync(
            () => _gitService.Branches.AbortMergeAsync(repository),
            _localizationService.GetString("AbortMergeSucceeded"));
    }

    private async Task ContinueOperationAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is not RepositoryInfo repository
            || !HasSequencerOperation)
        {
            return;
        }

        if (HasConflictedChanges)
        {
            ShowError(_localizationService.GetString("ResolveConflictsBeforeContinueOperation"));
            return;
        }

        GitOperationKind operationKind = OperationState.Kind;
        await RunSequencerOperationAsync(
            () => _gitService.ChangeRecovery.ContinueOperationAsync(
                repository,
                operationKind),
            _localizationService.GetString("ContinueOperationSucceeded"));
    }

    private async Task SkipOperationAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is not RepositoryInfo repository
            || !HasSequencerOperation)
        {
            return;
        }

        GitOperationKind operationKind = OperationState.Kind;
        bool confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("SkipOperationDialogTitle"),
            _localizationService.GetString("SkipOperationDialogMessage"),
            _localizationService.GetString("SkipOperationDialogPrimaryButton"));
        if (!confirmed)
        {
            return;
        }

        await RunSequencerOperationAsync(
            () => _gitService.ChangeRecovery.SkipOperationAsync(
                repository,
                operationKind),
            _localizationService.GetString("SkipOperationSucceeded"));
    }

    private async Task AbortOperationAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is not RepositoryInfo repository
            || !HasSequencerOperation)
        {
            return;
        }

        GitOperationKind operationKind = OperationState.Kind;
        bool confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("AbortOperationDialogTitle"),
            _localizationService.GetString("AbortOperationDialogMessage"),
            _localizationService.GetString("AbortOperationDialogPrimaryButton"));
        if (!confirmed)
        {
            return;
        }

        await RunSequencerOperationAsync(
            () => _gitService.ChangeRecovery.AbortOperationAsync(
                repository,
                operationKind),
            _localizationService.GetString("AbortOperationSucceeded"));
    }

    private async Task RunSequencerOperationAsync(
        System.Func<Task> operation,
        string successMessage)
    {
        await RunGitOperationAsync(async () =>
        {
            try
            {
                ClearResultMessages();
                await RunMutationAndRefreshStatusAsync(operation);
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
                ShowError(
                    _localizationService.GetString("GitOperationCommandFailed"),
                    exception.Message);
            }
        });
    }

    private async Task RunStagingOperationAsync(
        System.Func<Task> operation,
        string? pathToSelect,
        GitChangeState stateToSelect)
    {
        await RunGitOperationAsync(async () =>
        {
            try
            {
                await RunMutationAndRefreshStatusAsync(operation);

                if (!string.IsNullOrWhiteSpace(pathToSelect))
                {
                    SelectChange(pathToSelect, stateToSelect);
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
                ShowError(_localizationService.GetString("GitStageCommandFailed"), exception.Message);
            }
        });
    }

    private async Task RunDangerousOperationAsync(System.Func<Task> operation, string successMessage)
    {
        await RunGitOperationAsync(async () =>
        {
            try
            {
                ClearResultMessages();
                await RunMutationAndRefreshStatusAsync(operation);
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
                ShowError(_localizationService.GetString("GitDangerousOperationFailed"), exception.Message);
            }
        });
    }

    private async Task RunMutationAndRefreshStatusAsync(System.Func<Task> operation)
    {
        try
        {
            await operation();
        }
        finally
        {
            await RefreshStatusCoreAsync(clearResultMessages: false);
        }
    }

    private async Task RefreshStashesCoreAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            Stashes.Clear();
            SelectedStash = null;
            StashCount = 0;
            return;
        }

        var selectedReference = SelectedStash?.Reference;
        IReadOnlyList<GitStash> stashes = await _gitService.Stashes.GetStashesAsync(
            _mainWindowViewModel.CurrentRepository);
        Stashes.Clear();
        foreach (var stash in stashes)
        {
            Stashes.Add(stash);
        }

        SelectedStash = Stashes.FirstOrDefault(stash => stash.Reference == selectedReference) ?? Stashes.FirstOrDefault();
        StashCount = Stashes.Count;
    }

    private Task<bool> ConfirmStashOperationAsync(
        string titleKey,
        string messageKey,
        string primaryButtonKey,
        GitStash stash)
    {
        return _dialogService.ConfirmAsync(
            _localizationService.GetString(titleKey),
            string.Format(_localizationService.GetString(messageKey), stash.DisplayName),
            _localizationService.GetString(primaryButtonKey));
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

    private Task RunQueuedReadOperationAsync(System.Func<Task> operation)
    {
        return _gitService.ExecuteAsync(operation);
    }

    private void SelectChange(string path, GitChangeState state)
    {
        var target = state == GitChangeState.Staged
            ? StagedChanges.FirstOrDefault(item => item.Path == path)
            : UnstagedChanges.FirstOrDefault(item => item.Path == path);

        if (target is null)
        {
            return;
        }

        if (state == GitChangeState.Staged)
        {
            SelectedStagedChange = target;
        }
        else
        {
            SelectedUnstagedChange = target;
        }
    }

    private void ClearDiff(string message)
    {
        EndEditingFile();
        ConflictEditor.Clear();
        SelectedChanges = [];
        SelectedStagedChange = null;
        SelectedUnstagedChange = null;
        DiffLines.Clear();
        DiffText = "";
        SelectedDiffStat = DiffStat.Empty;
        DiffEmptyMessage = message;
        HasDiffEmptyState = true;
    }

    private void InitializeSyntaxHighlightingOptions()
    {
        SyntaxHighlightingOptions.Add(new DisplayOption<SyntaxHighlightingMode>(
            SyntaxHighlightingMode.Auto,
            _localizationService.GetString("SyntaxHighlightingAuto")));
        SyntaxHighlightingOptions.Add(new DisplayOption<SyntaxHighlightingMode>(
            SyntaxHighlightingMode.None,
            _localizationService.GetString("SyntaxHighlightingNone")));
        SyntaxHighlightingOptions.Add(new DisplayOption<SyntaxHighlightingMode>(
            SyntaxHighlightingMode.CStyle,
            _localizationService.GetString("SyntaxHighlightingCStyle")));
        SyntaxHighlightingOptions.Add(new DisplayOption<SyntaxHighlightingMode>(
            SyntaxHighlightingMode.Hash,
            _localizationService.GetString("SyntaxHighlightingHash")));
        SyntaxHighlightingOptions.Add(new DisplayOption<SyntaxHighlightingMode>(
            SyntaxHighlightingMode.Dash,
            _localizationService.GetString("SyntaxHighlightingDash")));
        SyntaxHighlightingOptions.Add(new DisplayOption<SyntaxHighlightingMode>(
            SyntaxHighlightingMode.Html,
            _localizationService.GetString("SyntaxHighlightingHtml")));
        SelectedSyntaxHighlightingOption = SyntaxHighlightingOptions.FirstOrDefault();
    }

    private string LocalizeDiffEmptyMessage(string message)
    {
        return message switch
        {
            "Diff is not available until the new file is staged." => _localizationService.GetString("NewFileDiffUnavailable"),
            "Binary file diff cannot be displayed." => _localizationService.GetString("BinaryDiffUnavailable"),
            "No textual diff is available for this file." => _localizationService.GetString("NoTextDiffAvailable"),
            _ => message
        };
    }

    private void ClearError()
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

    private void ClearResultMessages()
    {
        ClearNotification();
    }

    private void ShowSuccess(string message)
    {
        ShowNotification(AppNotificationSeverity.Success, message);
    }

    private void ShowSuccess(string message, string? details)
    {
        ShowNotification(AppNotificationSeverity.Success, message, details);
    }

    private static void ReplaceChanges(
        ObservableCollection<GitChangedFile> target,
        System.Collections.Generic.IReadOnlyList<GitChangedFile> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void ReplaceAllChanges()
    {
        AllChanges.Clear();

        var orderedChanges = StagedChanges
            .Concat(UnstagedChanges)
            .Concat(ConflictedChanges)
            .OrderBy(change => change.Path, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(change => change.State);

        foreach (var item in orderedChanges)
        {
            AllChanges.Add(item);
        }

        OnChangedFilesSummaryChanged();
    }

    private void UpdateCommandStates()
    {
        RefreshStatusCommand.NotifyCanExecuteChanged();
        StageSelectedCommand.NotifyCanExecuteChanged();
        UnstageSelectedCommand.NotifyCanExecuteChanged();
        StageAllCommand.NotifyCanExecuteChanged();
        UnstageAllCommand.NotifyCanExecuteChanged();
        OpenCommitDialogCommand.NotifyCanExecuteChanged();
        OpenAmendDialogCommand.NotifyCanExecuteChanged();
        AbortMergeCommand.NotifyCanExecuteChanged();
        ContinueOperationCommand.NotifyCanExecuteChanged();
        SkipOperationCommand.NotifyCanExecuteChanged();
        AbortOperationCommand.NotifyCanExecuteChanged();
        DiscardSelectedCommand.NotifyCanExecuteChanged();
        DiscardAllUnstagedCommand.NotifyCanExecuteChanged();
        CleanUntrackedCommand.NotifyCanExecuteChanged();
        CreateStashCommand.NotifyCanExecuteChanged();
        ApplyStashCommand.NotifyCanExecuteChanged();
        PopStashCommand.NotifyCanExecuteChanged();
        DropStashCommand.NotifyCanExecuteChanged();
        DropAllStashesCommand.NotifyCanExecuteChanged();
        StageChangeCommand.NotifyCanExecuteChanged();
        UnstageChangeCommand.NotifyCanExecuteChanged();
        DiscardChangeCommand.NotifyCanExecuteChanged();
        ToggleChangeDisplayModeCommand.NotifyCanExecuteChanged();
        ShowFullFileCommand.NotifyCanExecuteChanged();
        ShowDiffCommand.NotifyCanExecuteChanged();
        ToggleFullFileCommand.NotifyCanExecuteChanged();
        RevertDiffLineCommand.NotifyCanExecuteChanged();
        EditSelectedFileCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanOpenCommitDialog));
        OnPropertyChanged(nameof(CanRevertSelectedChange));
        OnPropertyChanged(nameof(CanEditSelectedFile));
        OnPropertyChanged(nameof(CanDiscardSelected));
        OnPropertyChanged(nameof(CanStageSelected));
        OnPropertyChanged(nameof(CanUnstageSelected));
    }

    private void SelectSingleChange(GitChangedFile change)
    {
        SelectedChanges = [change];

        if (change.State == GitChangeState.Staged)
        {
            SelectedStagedChange = change;
        }
        else
        {
            SelectedUnstagedChange = change;
        }
    }

    private void OnChangedFilesSummaryChanged()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ChangedFilesTitle));
        OnPropertyChanged(nameof(ChangedFilesStat));
    }

    private void OnDiffSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedDiffFileTitle));
        OnPropertyChanged(nameof(SelectedDiffFileTooltip));
        OnPropertyChanged(nameof(ConflictEditorVisibility));
        OnPropertyChanged(nameof(DiffViewerVisibility));
    }

    private async Task OnConflictResolvedAsync(string path)
    {
        await RefreshStatusCoreAsync(clearResultMessages: false);
        ShowSuccess(string.Format(
            _localizationService.GetString("ConflictMarkedResolved"),
            path));
    }

}

