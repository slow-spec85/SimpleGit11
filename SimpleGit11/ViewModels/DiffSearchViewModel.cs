using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.ViewModels;

public sealed class DiffSearchViewModel : AppNotificationViewModelBase
{
    private readonly ILocalizationService _localization;
    private readonly Func<string, bool, bool, CancellationToken, Task<IReadOnlyList<GitFileSearchResult>>> _search;
    private readonly Func<(GitCommit? Commit, GitChangedFile? File)> _currentFile;
    private readonly Func<GitFileSearchResult, GitFileSearchMatch, CancellationToken, Task<DiffLine?>> _showMatch;
    private readonly Action _filterFiles;
    private readonly ChangedFileTreeViewModel? _fileTree;
    private readonly RelayCommand _cancelSearchCommand;
    private CancellationTokenSource? _searchCancellation;
    private IReadOnlyList<GitFileSearchResult> _results = [];
    private HashSet<(string? Commit, string Path, GitChangeState? State)>? _matchingFiles;
    private IReadOnlyList<GitFileSearchResult> _visibleResults = [];
    private (GitFileSearchResult? File, int Match) _position;
    private string _query = "";
    private bool _canSearchHistory;
    private bool _searchHistory;
    private bool _changedLinesOnly;
    private bool _isBusy;
    private bool _searchCanceled;
    private string? _progressMessage;
    private (DiffLine Line, int Column, int Length)? _selection;

    internal DiffSearchViewModel(
        ILocalizationService localization,
        IMessenger messenger,
        bool canSearchHistory,
        Func<string, bool, bool, CancellationToken, Task<IReadOnlyList<GitFileSearchResult>>> search,
        Func<(GitCommit? Commit, GitChangedFile? File)> currentFile,
        Func<GitFileSearchResult, GitFileSearchMatch, CancellationToken, Task<DiffLine?>> showMatch,
        Action filterFiles,
        ChangedFileTreeViewModel? fileTree = null)
        : base(messenger)
    {
        _localization = localization;
        _canSearchHistory = canSearchHistory;
        _search = search;
        _currentFile = currentFile;
        _showMatch = showMatch;
        _filterFiles = filterFiles;
        _fileTree = fileTree;
        _cancelSearchCommand = new RelayCommand(CancelSearch);
    }

    public bool CanSearchHistory
    {
        get => _canSearchHistory;
        internal set => SetProperty(ref _canSearchHistory, value);
    }

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value)) { StartSearch(); }
        }
    }

    public bool SearchHistory
    {
        get => _searchHistory;
        set
        {
            if (CanSearchHistory && SetProperty(ref _searchHistory, value))
            {
                if (!value && MatchingCommitHashes is not null) { Invalidate(); }
                StartSearch();
            }
        }
    }

    public bool ChangedLinesOnly
    {
        get => _changedLinesOnly;
        set
        {
            if (SetProperty(ref _changedLinesOnly, value)) { StartSearch(); }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value)) { OnPropertyChanged(nameof(CanNavigate)); }
        }
    }

    public bool CanNavigate => !IsBusy && _visibleResults.Count > 0;
    internal bool HasFilters => Query.Length > 0 || SearchHistory || ChangedLinesOnly;
    internal bool IsFiltering => _matchingFiles is not null;
    internal IReadOnlySet<string>? MatchingCommitHashes { get; private set; }

    internal (DiffLine Line, int Column, int Length)? Selection
    {
        get => _selection;
        private set => SetProperty(ref _selection, value);
    }

    internal bool Includes(GitCommit? commit, GitChangedFile file) =>
        _matchingFiles?.Contains((commit?.Hash, file.Path, file.State)) ?? true;

    internal void SetVisibleCommits(IReadOnlyList<GitCommit> commits)
    {
        if (MatchingCommitHashes is null) { return; }
        ILookup<string, GitFileSearchResult> byCommit = _results
            .Where(result => result.Commit is not null)
            .ToLookup(result => result.Commit!.Hash, StringComparer.OrdinalIgnoreCase);
        _visibleResults = commits.SelectMany(commit => byCommit[commit.Hash]).ToArray();
        OnPropertyChanged(nameof(CanNavigate));
    }

    internal void Refresh() => _ = RefreshAsync();

    internal void ResetFilters()
    {
        SetProperty(ref _query, "", nameof(Query));
        SetProperty(ref _changedLinesOnly, false, nameof(ChangedLinesOnly));
        SetProperty(ref _searchHistory, false, nameof(SearchHistory));
        _searchCanceled = false;
        Invalidate();
    }

    internal bool RetryCanceledSearch()
    {
        if (!_searchCanceled) { return false; }
        StartSearch();
        return true;
    }

    private void StartSearch()
    {
        _searchCanceled = false;
        Refresh();
    }

    private void CancelSearch()
    {
        if (_progressMessage is null) { return; }
        _searchCanceled = true;
        Invalidate();
        ShowNotification(AppNotificationSeverity.Informational, _localization.GetString("DiffFileSearchCanceled"));
    }

    private void SetSearchProgress(string? message)
    {
        if (_progressMessage == message) { return; }
        _progressMessage = message;
        PublishOperationState(message is not null, message, message is null ? null : _cancelSearchCommand);
    }

    internal void Invalidate()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
        SetSearchProgress(null);
        _position = default;
        Selection = null;
        ReplaceResults([], applyFilter: false);
        ClearNotification();
        IsBusy = false;
    }

    internal async Task RefreshAsync()
    {
        if (_searchCanceled) { return; }
        if (Query.Length == 0)
        {
            Invalidate();
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _searchCancellation.Token;
        _position = default;
        Selection = null;
        string query = Query;
        bool changedLinesOnly = ChangedLinesOnly;
        bool searchHistory = SearchHistory;
        IsBusy = true;
        ClearNotification();
        try
        {
            SetSearchProgress(_localization.GetString(searchHistory ? "DiffHistorySearchRunning" : "DiffFileSearchRunning"));
            await Task.Delay(searchHistory ? 400 : 180, cancellationToken);
            IReadOnlyList<GitFileSearchResult> results =
                await _search(query, changedLinesOnly, searchHistory, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            SetSearchProgress(null);

            (GitCommit? commit, GitChangedFile? file) = _currentFile();
            ReplaceResults(results, applyFilter: true, searchHistory: searchHistory);
            cancellationToken.ThrowIfCancellationRequested();
            if (_visibleResults.Count > 0)
            {
                int current = FindFile(commit, file);
                await ShowAsync(current < 0 ? 0 : current, 0, cancellationToken);
            }

            string message = _visibleResults.Count == 0
                ? _localization.GetString("DiffFileSearchNoMatches")
                : string.Format(CultureInfo.CurrentCulture, _localization.GetString("DiffFileSearchMatchesFound"),
                    _visibleResults.Sum(result => (long)result.Matches.Count));
            ShowNotification(AppNotificationSeverity.Informational, message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsSearchFailure(exception))
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                ReplaceResults([], applyFilter: false);
                ShowNotification(AppNotificationSeverity.Informational, _localization.GetString("DiffFileSearchFailed"));
            }
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                SetSearchProgress(null);
                IsBusy = false;
            }
        }
    }

    internal async Task MoveAsync(bool forward)
    {
        if (!CanNavigate || _searchCancellation is null) { return; }
        CancellationToken cancellationToken = _searchCancellation.Token;
        (GitCommit? commit, GitChangedFile? file) = _currentFile();
        int current = FindFile(commit, file);
        (int fileIndex, int matchIndex) = GetNextPosition(current, forward);

        IsBusy = true;
        try
        {
            await ShowAsync(fileIndex, matchIndex, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsSearchFailure(exception))
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                ShowNotification(AppNotificationSeverity.Informational, _localization.GetString("DiffFileSearchFailed"));
            }
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested) { IsBusy = false; }
        }
    }

    private (int File, int Match) GetNextPosition(int current, bool forward)
    {
        if (current < 0) { current = forward ? 0 : _visibleResults.Count - 1; }
        if (!ReferenceEquals(_position.File, _visibleResults[current]))
        {
            return (current, forward ? 0 : _visibleResults[current].Matches.Count - 1);
        }

        int nextMatch = _position.Match + (forward ? 1 : -1);
        if (nextMatch >= 0 && nextMatch < _visibleResults[current].Matches.Count)
        {
            return (current, nextMatch);
        }

        int nextFile = (current + (forward ? 1 : _visibleResults.Count - 1)) % _visibleResults.Count;
        return (nextFile, forward ? 0 : _visibleResults[nextFile].Matches.Count - 1);
    }

    private int FindFile(GitCommit? commit, GitChangedFile? file)
    {
        if (file is null) { return -1; }
        for (int index = 0; index < _visibleResults.Count; index++)
        {
            GitFileSearchResult result = _visibleResults[index];
            if (result.Commit?.Hash == commit?.Hash && result.File.Path == file.Path && result.File.State == file.State)
            {
                return index;
            }
        }

        return -1;
    }

    private async Task ShowAsync(int fileIndex, int matchIndex, CancellationToken cancellationToken)
    {
        GitFileSearchResult result = _visibleResults[fileIndex];
        GitFileSearchMatch match = result.Matches[matchIndex];
        DiffLine? line = await _showMatch(result, match, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _position = (result, matchIndex);
        Selection = line is null ? null : (line, match.Column, match.Length);
    }

    private void ReplaceResults(IReadOnlyList<GitFileSearchResult> results, bool applyFilter, bool searchHistory = false)
    {
        bool wasFiltering = IsFiltering;
        _results = _fileTree?.OrderSearchResults(results) ?? results;
        _visibleResults = _results;
        MatchingCommitHashes = searchHistory ? results
            .Where(result => result.Commit is not null)
            .Select(result => result.Commit!.Hash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        _matchingFiles = applyFilter ? results.Select(result => (result.Commit?.Hash, result.File.Path, result.File.State))
            .ToHashSet() : null;
        if (wasFiltering || applyFilter) { _filterFiles(); }
        OnPropertyChanged(nameof(CanNavigate));
    }

    internal static DiffLine? FindLine(
        IList<DiffLine> lines, GitChangedFile file, GitFileSearchMatch match, string query)
    {
        if (match.IsDiffRow)
        {
            return match.Line < lines.Count && IsMatch(lines[match.Line]) ? lines[match.Line] : null;
        }

        bool deleted = file.Status == "Deleted";
        return lines.FirstOrDefault(line =>
            (deleted ? line.OldLineNumber ?? line.SourceLineNumber : line.SourceLineNumber) == match.Line
            && (deleted || line.Kind != DiffLineKind.Removed) && IsMatch(line));

        bool IsMatch(DiffLine line) =>
            match.Column + match.Length <= line.Text.Length
            && line.Text.AsSpan(match.Column, match.Length).Equals(query.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSearchFailure(Exception exception) =>
        exception is GitCommandException or IOException or UnauthorizedAccessException or InvalidOperationException;
}
