using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;

namespace SimpleGit11.ViewModels;

public abstract partial class CommitDetailsViewModel : AppNotificationViewModelBase
{
    protected readonly MainWindowViewModel _mainWindowViewModel;
    protected readonly IGitService _gitService;
    protected readonly ILocalizationService _localizationService;
    protected readonly IAsyncCommandExecutor _asyncCommandExecutor;

    private readonly ISettingsService _settingsService;
    private readonly IClipboardService _clipboardService;
    private readonly string _noCommitSelectedMessageKey;
    private readonly string _selectCommitFileMessageKey;
    private readonly string _openRepositoryMessageKey;
    private readonly List<GitChangedFile> _allChangedFiles = [];

    protected CommitDetailsViewModel(
        MainWindowViewModel mainWindowViewModel,
        IGitService gitService,
        ILocalizationService localizationService,
        IClipboardService clipboardService,
        IAsyncCommandExecutor asyncCommandExecutor,
        ISettingsService settingsService,
        IMessenger messenger,
        string noCommitSelectedMessageKey,
        string selectCommitFileMessageKey,
        string openRepositoryMessageKey)
        : base(messenger)
    {
        _mainWindowViewModel = mainWindowViewModel;
        _gitService = gitService;
        _localizationService = localizationService;
        _clipboardService = clipboardService;
        _asyncCommandExecutor = asyncCommandExecutor;
        _settingsService = settingsService;
        _noCommitSelectedMessageKey = noCommitSelectedMessageKey;
        _selectCommitFileMessageKey = selectCommitFileMessageKey;
        _openRepositoryMessageKey = openRepositoryMessageKey;
        DiffEmptyMessage = _localizationService.GetString(_noCommitSelectedMessageKey);
        DiffText = "";
        HasDiffEmptyState = true;
        SelectedDiffStat = DiffStat.Empty;

        InitializeSyntaxHighlightingOptions();
    }

    public ObservableCollection<GitChangedFile> ChangedFiles { get; } = [];
    public ObservableCollection<DiffLine> DiffLines { get; } = [];
    public ObservableCollection<DisplayOption<SyntaxHighlightingMode>> SyntaxHighlightingOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCommit))]
    [NotifyPropertyChangedFor(nameof(SelectedCommitDetailsVisibility))]
    [NotifyPropertyChangedFor(nameof(SelectedCommitHash))]
    [NotifyPropertyChangedFor(nameof(SelectedCommitAuthor))]
    [NotifyPropertyChangedFor(nameof(SelectedCommitDate))]
    [NotifyPropertyChangedFor(nameof(SelectedCommitMessage))]
    public partial GitCommit? SelectedCommit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDiffFileTitle))]
    [NotifyPropertyChangedFor(nameof(SelectedDiffFileTooltip))]
    public partial GitChangedFile? SelectedChangedFile { get; set; }

    partial void OnSelectedCommitChanged(GitCommit? value)
    {
        OnSelectedCommitChanged();
        RaiseCommitDetailsCommandCanExecuteChanged();
        ClearCommitDetails(value is null
            ? _localizationService.GetString(_noCommitSelectedMessageKey)
            : _localizationService.GetString(_selectCommitFileMessageKey));

        if (value is not null)
        {
            _ = LoadCommitDetailsAsync(value);
        }
    }

    partial void OnSelectedChangedFileChanged(GitChangedFile? value)
    {
        RaiseCommitDetailsCommandCanExecuteChanged();
        if (value is not null && SelectedCommit is GitCommit commit)
        {
            _ = LoadSelectedFileAsync(commit, value);
        }
    }

    public DiffStat ChangedFilesStat => DiffStat.Sum(ChangedFiles.Select(file => file.Stat));
    public string ChangedFilesTitle => PluralizationService.FormatChangeCount(ChangedFiles.Count, _localizationService);

    [ObservableProperty]
    public partial DiffStat SelectedDiffStat { get; private set; }

    public string SelectedDiffFileTitle => SelectedChangedFile?.FileName ?? "";
    public string SelectedDiffFileTooltip => SelectedChangedFile?.Path ?? "";
    public bool HasSelectedCommit => SelectedCommit is not null;
    public Visibility SelectedCommitDetailsVisibility => HasSelectedCommit ? Visibility.Visible : Visibility.Collapsed;
    public string SelectedCommitHash => SelectedCommit?.Hash ?? "";
    public string SelectedCommitAuthor
    {
        get
        {
            if (SelectedCommit is not GitCommit commit)
            {
                return "";
            }

            if (!commit.HasDistinctCommitter)
            {
                return commit.DisplayAuthor;
            }

            string committer = string.Format(
                _localizationService.GetString("CommitterIdentityFormat"),
                commit.DisplayCommitter);
            return $"{commit.DisplayAuthor}  •  {committer}";
        }
    }
    public string SelectedCommitDate => SelectedCommit?.DisplayDate ?? "";
    public string SelectedCommitMessage => SelectedCommit?.Message ?? "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangedFilesBlockVisibility))]
    [NotifyPropertyChangedFor(nameof(CommitDetailsBlockVisibility))]
    public partial bool IsCommitDetailsBlockVisible { get; private set; }

    public Visibility ChangedFilesBlockVisibility => IsCommitDetailsBlockVisible
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility CommitDetailsBlockVisibility => IsCommitDetailsBlockVisible
        ? Visibility.Visible
        : Visibility.Collapsed;

    [ObservableProperty]
    public partial bool IsDiffLoading { get; private set; }

    [ObservableProperty]
    public partial bool HasDiffEmptyState { get; private set; }

    [ObservableProperty]
    public partial string DiffEmptyMessage { get; private set; }

    [ObservableProperty]
    public partial string DiffText { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiffModeToggleText))]
    [NotifyPropertyChangedFor(nameof(ContextMenuDisplayModeText))]
    public partial bool IsFullFileMode { get; private set; }

    partial void OnIsFullFileModeChanged(bool value)
    {
        RaiseCommitDetailsCommandCanExecuteChanged();
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
            if (SelectedCommit is not null && SelectedChangedFile is not null)
            {
                _ = LoadSelectedFileAsync(SelectedCommit, SelectedChangedFile);
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

    protected abstract bool IsCommitDetailsOperationRunning { get; }

    protected abstract void ShowCommitDetailsError(string message, string? details = null);

    protected virtual void ClearCommitDetailsError()
    {
    }

    protected virtual void OnSelectedCommitChanged()
    {
    }

    protected virtual bool ShouldIncludeChangedFile(GitChangedFile file)
    {
        return true;
    }

    protected void RefreshChangedFilesFilter()
    {
        ApplyChangedFilesFilter();
    }

    protected void ClearCommitDetails()
    {
        ClearCommitDetails(_localizationService.GetString(_noCommitSelectedMessageKey));
    }

    protected void RaiseCommitDetailsCommandCanExecuteChanged()
    {
        ShowChangedFileFullFileCommand.NotifyCanExecuteChanged();
        ShowFullFileCommand.NotifyCanExecuteChanged();
        ShowDiffCommand.NotifyCanExecuteChanged();
        ToggleFullFileCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadCommitDetailsAsync(GitCommit commit)
    {
        await RunQueuedReadOperationAsync(async () =>
        {
            if (_mainWindowViewModel.CurrentRepository is null)
            {
                ShowCommitDetailsError(_localizationService.GetString(_openRepositoryMessageKey));
                return;
            }

            try
            {
                IReadOnlyList<GitChangedFile> files = await _gitService.History.GetChangedFilesAsync(
                    _mainWindowViewModel.CurrentRepository,
                    commit);
                if (!ReferenceEquals(SelectedCommit, commit))
                {
                    return;
                }

                ReplaceChangedFiles(files);
            }
            catch (FileNotFoundException)
            {
                ShowCommitDetailsError(_localizationService.GetString("GitExecutableNotFound"));
            }
            catch (DirectoryNotFoundException)
            {
                ShowCommitDetailsError(_localizationService.GetString("RepositoryFolderNotFound"));
            }
            catch (GitCommandException exception)
            {
                ShowCommitDetailsError(_localizationService.GetString("GitShowCommandFailed"), exception.Message);
            }
        });
    }

    private async Task LoadDiffAsync(GitCommit commit, GitChangedFile changedFile)
    {
        await RunQueuedReadOperationAsync(async () =>
        {
            if (!ReferenceEquals(SelectedCommit, commit) ||
                !ReferenceEquals(SelectedChangedFile, changedFile))
            {
                return;
            }

            if (_mainWindowViewModel.CurrentRepository is null)
            {
                ShowCommitDetailsError(_localizationService.GetString(_openRepositoryMessageKey));
                return;
            }

            try
            {
                await LoadDiffCoreAsync(commit, changedFile);
            }
            catch (FileNotFoundException)
            {
                ShowCommitDetailsError(_localizationService.GetString("GitExecutableNotFound"));
            }
            catch (DirectoryNotFoundException)
            {
                ShowCommitDetailsError(_localizationService.GetString("RepositoryFolderNotFound"));
            }
            catch (GitCommandException exception)
            {
                ShowCommitDetailsError(_localizationService.GetString("GitDiffCommandFailed"), exception.Message);
            }
        });
    }

    private Task LoadSelectedFileAsync(GitCommit commit, GitChangedFile changedFile)
    {
        if (!changedFile.CanShowFileContent)
        {
            IsFullFileMode = false;
            return LoadDiffAsync(commit, changedFile);
        }

        return IsFullFileMode
            ? ShowFullFileCoreAsync(commit, changedFile)
            : LoadDiffAsync(commit, changedFile);
    }

    private async Task LoadDiffCoreAsync(GitCommit commit, GitChangedFile changedFile)
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            return;
        }

        IsDiffLoading = true;
        try
        {
            DiffResult diff = await _gitService.Diff.GetCommitDiffAsync(
                _mainWindowViewModel.CurrentRepository,
                commit,
                changedFile);

            DiffLines.Clear();
            foreach (DiffLine line in diff.Lines)
            {
                DiffLines.Add(line);
            }

            SelectedDiffStat = diff.Stat;
            DiffText = DiffTextFormatter.FormatText(diff.Lines);
            HasDiffEmptyState = diff.IsEmpty;
            DiffEmptyMessage = diff.IsEmpty ? LocalizeDiffEmptyMessage(diff.EmptyMessage) : "";
        }
        finally
        {
            IsDiffLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanShowFullFile), FlowExceptionsToTaskScheduler = true)]
    private Task OnShowFullFileAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(ShowFullFileCoreAsync);
    }

    private bool CanShowFullFile()
    {
        return SelectedCommit is not null
            && SelectedChangedFile?.CanShowFileContent == true
            && !IsCommitDetailsOperationRunning;
    }

    private Task ShowFullFileCoreAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedCommit is null || SelectedChangedFile is null)
        {
            return Task.CompletedTask;
        }

        return ShowFullFileCoreAsync(SelectedCommit, SelectedChangedFile);
    }

    private async Task ShowFullFileCoreAsync(GitCommit commit, GitChangedFile changedFile)
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            return;
        }

        if (!changedFile.CanShowFileContent)
        {
            IsFullFileMode = false;
            await LoadDiffAsync(commit, changedFile);
            return;
        }

        IsFullFileMode = true;
        RepositoryInfo repository = _mainWindowViewModel.CurrentRepository;
        await RunQueuedReadOperationAsync(async () =>
        {
            if (!ReferenceEquals(SelectedCommit, commit) ||
                !ReferenceEquals(SelectedChangedFile, changedFile))
            {
                return;
            }

            try
            {
                ClearCommitDetailsError();
                IsDiffLoading = true;
                Task<DiffResult> diffTask = _gitService.Diff.GetCommitDiffAsync(repository, commit, changedFile);
                Task<string> textTask = _gitService.Diff.GetCommitFileTextAsync(repository, commit, changedFile);
                await Task.WhenAll(diffTask, textTask);

                DiffResult diff = await diffTask;
                string text = await textTask;
                IReadOnlyList<DiffLine> fullFileLines = DiffTextFormatter.FormatFullFile(
                    text,
                    diff.Lines,
                    changedFile.Status == "Deleted");
                DiffLines.Clear();
                foreach (DiffLine line in fullFileLines)
                {
                    DiffLines.Add(line);
                }

                SelectedDiffStat = diff.Stat;
                DiffText = DiffTextFormatter.FormatText(fullFileLines);
                HasDiffEmptyState = string.IsNullOrWhiteSpace(text);
                DiffEmptyMessage = HasDiffEmptyState
                    ? _localizationService.GetString("NoCommitDiffAvailable")
                    : "";
            }
            catch (FileNotFoundException)
            {
                ShowCommitDetailsError(_localizationService.GetString("GitExecutableNotFound"));
            }
            catch (DirectoryNotFoundException)
            {
                ShowCommitDetailsError(_localizationService.GetString("RepositoryFolderNotFound"));
            }
            catch (GitCommandException exception)
            {
                ShowCommitDetailsError(_localizationService.GetString("GitShowCommandFailed"), exception.Message);
            }
            finally
            {
                IsDiffLoading = false;
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanShowChangedFileFullFile), FlowExceptionsToTaskScheduler = true)]
    private Task OnShowChangedFileFullFileAsync(GitChangedFile? changedFile)
    {
        return _asyncCommandExecutor.ExecuteAsync(
            () => ShowChangedFileFullFileCoreAsync(changedFile));
    }

    private bool CanShowChangedFileFullFile(GitChangedFile? changedFile)
    {
        return changedFile is not null
            && changedFile.CanShowFileContent
            && SelectedCommit is not null
            && !IsCommitDetailsOperationRunning;
    }

    private Task ShowChangedFileFullFileCoreAsync(GitChangedFile? changedFile)
    {
        if (changedFile is null)
        {
            return Task.CompletedTask;
        }

        IsFullFileMode = true;
        if (ReferenceEquals(SelectedChangedFile, changedFile))
        {
            return ShowFullFileCoreAsync();
        }

        SelectedChangedFile = changedFile;
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanShowDiff), FlowExceptionsToTaskScheduler = true)]
    private Task OnShowDiffAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(ShowDiffCoreAsync);
    }

    private bool CanShowDiff()
    {
        return SelectedCommit is not null && !IsCommitDetailsOperationRunning;
    }

    private async Task ShowDiffCoreAsync()
    {
        if (SelectedCommit is not null && SelectedChangedFile is not null)
        {
            IsFullFileMode = false;
            await LoadDiffAsync(SelectedCommit, SelectedChangedFile);
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggleFullFile), FlowExceptionsToTaskScheduler = true)]
    private Task OnToggleFullFileAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(ToggleFullFileCoreAsync);
    }

    private bool CanToggleFullFile()
    {
        return SelectedCommit is not null
            && SelectedChangedFile?.CanShowFileContent == true
            && !IsCommitDetailsOperationRunning;
    }

    private async Task ToggleFullFileCoreAsync()
    {
        if (IsFullFileMode)
        {
            await ShowDiffCoreAsync();
        }
        else
        {
            await ShowFullFileCoreAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggleChangeDisplayMode), FlowExceptionsToTaskScheduler = true)]
    private Task OnToggleChangeDisplayModeAsync(GitChangedFile? change)
    {
        return _asyncCommandExecutor.ExecuteAsync(
            () => ToggleChangeDisplayModeCoreAsync(change));
    }

    private bool CanToggleChangeDisplayMode(GitChangedFile? change)
    {
        return change?.CanShowFileContent == true && !IsCommitDetailsOperationRunning;
    }

    private Task ToggleChangeDisplayModeCoreAsync(GitChangedFile? change)
    {
        if (change is null)
        {
            return Task.CompletedTask;
        }

        bool showFullFile = !IsFullFileMode;
        IsFullFileMode = showFullFile;
        if (ReferenceEquals(SelectedChangedFile, change))
        {
            return showFullFile
                ? ShowFullFileCoreAsync()
                : ShowDiffCoreAsync();
        }

        SelectedChangedFile = change;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void OnShowChangedFilesBlock()
    {
        IsCommitDetailsBlockVisible = false;
    }

    [RelayCommand]
    private void OnShowCommitDetailsBlock()
    {
        IsCommitDetailsBlockVisible = true;
    }

    [RelayCommand]
    private void OnCopyText(string? text)
    {
        if (text is not null)
        {
            _clipboardService.SetText(text);
        }
    }

    private void ReplaceChangedFiles(IReadOnlyList<GitChangedFile> files)
    {
        _allChangedFiles.Clear();
        _allChangedFiles.AddRange(files);
        ApplyChangedFilesFilter();
    }

    private void ApplyChangedFilesFilter()
    {
        ChangedFiles.Clear();
        foreach (GitChangedFile file in _allChangedFiles)
        {
            if (ShouldIncludeChangedFile(file))
            {
                ChangedFiles.Add(file);
            }
        }

        SelectedChangedFile = ChangedFiles.FirstOrDefault();
        OnDiffSelectionChanged();
        OnChangedFilesSummaryChanged();
    }

    private void ClearCommitDetails(string message)
    {
        SelectedChangedFile = null;
        _allChangedFiles.Clear();
        ChangedFiles.Clear();
        DiffLines.Clear();
        DiffText = "";
        SelectedDiffStat = DiffStat.Empty;
        DiffEmptyMessage = message;
        HasDiffEmptyState = true;
        OnDiffSelectionChanged();
        OnChangedFilesSummaryChanged();
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
            "Binary file diff cannot be displayed." => _localizationService.GetString("BinaryDiffUnavailable"),
            "No textual diff is available for this commit." => _localizationService.GetString("NoCommitDiffAvailable"),
            _ => message
        };
    }

    private Task RunQueuedReadOperationAsync(System.Func<Task> operation)
    {
        return _gitService.ExecuteAsync(operation);
    }

    private void OnChangedFilesSummaryChanged()
    {
        OnPropertyChanged(nameof(ChangedFilesTitle));
        OnPropertyChanged(nameof(ChangedFilesStat));
    }

    private void OnDiffSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedDiffFileTitle));
        OnPropertyChanged(nameof(SelectedDiffFileTooltip));
    }
}
