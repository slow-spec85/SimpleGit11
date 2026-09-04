using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Presentation.Commits;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;

namespace SimpleGit11.ViewModels;

public abstract partial class CommitBrowserViewModelBase : CommitDetailsViewModel
{
    protected const int CommitPageSize = 300;
    private readonly List<GitCommit> _allCommits = [];
    private string _exactFilePathSearchText = "";

    protected CommitBrowserViewModelBase(
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
        : base(
            mainWindowViewModel,
            gitService,
            localizationService,
            clipboardService,
            asyncCommandExecutor,
            settingsService,
            messenger,
            noCommitSelectedMessageKey,
            selectCommitFileMessageKey,
            openRepositoryMessageKey)
    {
        SearchText = "";
        FilterFromTime = TimeSpan.Zero;
        FilterToTime = new TimeSpan(23, 59, 0);
    }

    public ObservableCollection<GitCommit> Commits { get; } = [];

    public ObservableCollection<GitCommit> SelectedCommits { get; } = [];

    public ObservableCollection<CommitParentViewItem> ParentCommits { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCommitFilterApplied))]
    public partial string SearchText { get; set; }

    partial void OnSearchTextChanged(string value)
    {
        if (!string.IsNullOrEmpty(_exactFilePathSearchText)
            && !string.Equals(_exactFilePathSearchText, value, StringComparison.Ordinal))
        {
            _exactFilePathSearchText = "";
        }

        ApplyFilters();
        RefreshChangedFilesFilter();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCommitFilterApplied))]
    public partial bool IsMainlineOnly { get; set; }

    partial void OnIsMainlineOnlyChanged(bool value)
    {
        ApplyFilters();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCommitFilterApplied))]
    [NotifyPropertyChangedFor(nameof(IsFilterFromTimeEnabled))]
    public partial DateTimeOffset? FilterFromDate { get; set; }

    partial void OnFilterFromDateChanged(DateTimeOffset? value)
    {
        ApplyFilters();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterFromTimeText))]
    public partial TimeSpan FilterFromTime { get; set; }

    partial void OnFilterFromTimeChanged(TimeSpan value)
    {
        ApplyFilters();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCommitFilterApplied))]
    [NotifyPropertyChangedFor(nameof(IsFilterToTimeEnabled))]
    public partial DateTimeOffset? FilterToDate { get; set; }

    partial void OnFilterToDateChanged(DateTimeOffset? value)
    {
        ApplyFilters();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterToTimeText))]
    public partial TimeSpan FilterToTime { get; set; }

    partial void OnFilterToTimeChanged(TimeSpan value)
    {
        ApplyFilters();
    }

    public bool IsFilterFromTimeEnabled => FilterFromDate.HasValue;

    public bool IsFilterToTimeEnabled => FilterToDate.HasValue;

    public string FilterFromTimeText => FilterFromTime.ToString(@"hh\:mm");

    public string FilterToTimeText => FilterToTime.ToString(@"hh\:mm");

    public bool IsCommitFilterApplied => CreateFilterCriteria().IsApplied;

    [RelayCommand]
    private void OnClearFilterFromDate()
    {
        FilterFromDate = null;
        FilterFromTime = TimeSpan.Zero;
    }

    [RelayCommand]
    private void OnClearFilterToDate()
    {
        FilterToDate = null;
        FilterToTime = new TimeSpan(23, 59, 0);
    }

    [RelayCommand]
    private void OnResetCommitFilters()
    {
        ClearCommitFilters();
    }

    public bool HasParentCommits => ParentCommits.Count > 0;

    public virtual Visibility EditCommitMessageActionVisibility => Visibility.Collapsed;

    public string CommitsTitle => PluralizationService.FormatCommitCount(
        Commits.Count,
        _localizationService);

    public string LoadMoreCommitsButtonText => string.Format(
        _localizationService.GetString("LoadMoreCommitsButtonText"),
        CommitPageSize);

    protected IReadOnlyList<GitCommit> AllCommits => _allCommits;

    protected bool HasUnfilteredCommits => _allCommits.Count > 0;

    protected virtual bool CanEditCommitMessage()
    {
        return false;
    }

    protected virtual Task EditCommitMessageCoreAsync()
    {
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanEditCommitMessage), FlowExceptionsToTaskScheduler = true)]
    private Task OnEditCommitMessageAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(EditCommitMessageCoreAsync);
    }

    protected void ReplaceCommits(IEnumerable<GitCommit> commits)
    {
        _allCommits.Clear();
        _allCommits.AddRange(commits);
        ApplyFilters();
    }

    protected void ClearCommits()
    {
        _allCommits.Clear();
        Commits.Clear();
        SetSelectedCommits([]);
        SelectedCommit = null;
        RefreshParentCommits();
        OnPropertyChanged(nameof(CommitsTitle));
        OnCommitFilterChanged();
    }

    protected void AppendCommits(IEnumerable<GitCommit> commits)
    {
        HashSet<string> existingHashes = _allCommits
            .Select(commit => commit.Hash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (GitCommit commit in commits)
        {
            if (!existingHashes.Add(commit.Hash))
            {
                continue;
            }

            _allCommits.Add(commit);
        }

        ApplyFilters();
    }

    protected virtual void OnCommitFilterChanged()
    {
    }

    protected virtual void OnBrowserSelectedCommitChanged()
    {
    }

    protected virtual void OnCommitSelectionChanged()
    {
    }

    public void SetSelectedCommits(IEnumerable<GitCommit> commits)
    {
        SelectedCommits.Clear();
        foreach (GitCommit commit in commits)
        {
            SelectedCommits.Add(commit);
        }

        OnCommitSelectionChanged();
    }

    protected sealed override void OnSelectedCommitChanged()
    {
        RefreshParentCommits();
        OnBrowserSelectedCommitChanged();
    }

    protected override bool ShouldIncludeChangedFile(GitChangedFile file)
    {
        return !IsExactFilePathSearch
            || string.Equals(file.Path, _exactFilePathSearchText, StringComparison.Ordinal);
    }

    private void ApplyFilters()
    {
        string? selectedHash = SelectedCommit?.Hash;
        IReadOnlyList<GitCommit> filteredCommits = CommitBrowserFilter.Apply(
            _allCommits,
            CreateFilterCriteria());

        Commits.Clear();
        foreach (GitCommit commit in filteredCommits)
        {
            Commits.Add(commit);
        }

        SelectedCommit = Commits.FirstOrDefault(commit =>
            string.Equals(commit.Hash, selectedHash, StringComparison.OrdinalIgnoreCase))
            ?? Commits.FirstOrDefault();
        OnPropertyChanged(nameof(CommitsTitle));
        OnCommitFilterChanged();
    }

    private CommitFilterCriteria CreateFilterCriteria() => new(
        IsMainlineOnly,
        FilterFromDate,
        FilterFromTime,
        FilterToDate,
        FilterToTime,
        SearchText,
        _exactFilePathSearchText);

    [RelayCommand]
    private void OnFilterByChangedFile(GitChangedFile? changedFile)
    {
        if (changedFile is null)
        {
            return;
        }

        string filePath = changedFile.Path;
        _exactFilePathSearchText = filePath;
        if (string.Equals(SearchText, filePath, StringComparison.Ordinal))
        {
            ApplyFilters();
            RefreshChangedFilesFilter();
            return;
        }

        SearchText = filePath;
    }

    private bool IsExactFilePathSearch => !string.IsNullOrEmpty(_exactFilePathSearchText)
        && string.Equals(_exactFilePathSearchText, SearchText, StringComparison.Ordinal);

    [RelayCommand]
    private void OnNavigateToParentCommit(CommitParentViewItem? parent)
    {
        if (parent is null)
        {
            return;
        }

        GitCommit? parentCommit = _allCommits.FirstOrDefault(commit =>
            string.Equals(commit.Hash, parent.Hash, StringComparison.OrdinalIgnoreCase));
        if (parentCommit is null)
        {
            ShowNotification(
                AppNotificationSeverity.Informational,
                string.Format(_localizationService.GetString("ParentCommitNotLoaded"), parent.Hash));
            return;
        }

        ClearCommitFilters();

        SelectedCommit = Commits.FirstOrDefault(commit =>
            string.Equals(commit.Hash, parent.Hash, StringComparison.OrdinalIgnoreCase));
    }

    private void ClearCommitFilters()
    {
        IsMainlineOnly = false;
        FilterFromDate = null;
        FilterFromTime = TimeSpan.Zero;
        FilterToDate = null;
        FilterToTime = new TimeSpan(23, 59, 0);
        SearchText = "";
    }

    private void RefreshParentCommits()
    {
        ParentCommits.Clear();
        if (SelectedCommit is not null)
        {
            for (int index = 0; index < SelectedCommit.ParentHashes.Count; index++)
            {
                string parentHash = SelectedCommit.ParentHashes[index];
                GitCommit? parentCommit = _allCommits.FirstOrDefault(commit =>
                    string.Equals(commit.Hash, parentHash, StringComparison.OrdinalIgnoreCase));
                string relationshipKey = index == 0
                    ? "CommitParentMainline"
                    : "CommitParentMergedHistory";

                ParentCommits.Add(new CommitParentViewItem(
                    parentHash,
                    _localizationService.GetString(relationshipKey),
                    parentCommit,
                    _localizationService.GetString("CommitParentUnavailable"),
                    _localizationService.GetString("CommitParentTooltipTitle"),
                    index > 0));
            }
        }

        OnPropertyChanged(nameof(HasParentCommits));
    }
}
