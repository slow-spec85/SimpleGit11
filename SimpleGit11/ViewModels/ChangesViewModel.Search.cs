using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.ViewModels;

public sealed partial class ChangesViewModel
{
    private bool _conflictSearchOpened;

    public DiffSearchViewModel DiffSearch { get; }

    private bool ShowConflictSearchPreview =>
        SelectedChange?.IsConflicted == true && (_conflictSearchOpened || DiffSearch.IsFiltering) && !ConflictEditor.IsDirty;

    internal bool BeginConflictSearch()
    {
        if (ConflictEditor.IsDirty || SelectedChange is not { IsConflicted: true } change) { return false; }
        _conflictSearchOpened = true;
        OnDiffSelectionChanged();
        _ = LoadDiffAsync(change);
        return true;
    }

    internal void EndConflictSearch()
    {
        if (!_conflictSearchOpened) { return; }
        _conflictSearchOpened = false;
        if (!ShowConflictSearchPreview) { FilterSearchResults(); }
    }

    private Task<IReadOnlyList<GitFileSearchResult>> SearchChangedFilesAsync(
        string query, bool changedLinesOnly, bool searchHistory, CancellationToken cancellationToken)
    {
        if (_mainWindowViewModel.CurrentRepository is not RepositoryInfo repository || IsEditingFile || ConflictEditor.IsDirty)
        {
            return Task.FromResult<IReadOnlyList<GitFileSearchResult>>([]);
        }

        return _gitService.Diff.SearchFilesAsync(
            repository, null, AllChanges.ToArray(), query, changedLinesOnly, cancellationToken, SelectedChange);
    }

    private void FilterSearchResults()
    {
        RefreshVisibleChanges();
        OnPropertyChanged(nameof(IsFilePathFilterEnabled));
        OnPropertyChanged(nameof(IsFileFilterApplied));
        OnDiffSelectionChanged();
        if (!DiffSearch.IsFiltering && SelectedChange is { IsConflicted: true } change)
        {
            _ = LoadSelectedChangeAsync(change);
        }
    }

    private async Task<DiffLine?> ShowSearchMatchAsync(
        GitFileSearchResult result, GitFileSearchMatch match, CancellationToken cancellationToken)
    {
        if (IsEditingFile || ConflictEditor.IsDirty) { return null; }
        GitChangedFile? file = AllChanges.FirstOrDefault(change =>
            change.Path == result.File.Path && change.State == result.File.State);
        if (file is null) { return null; }

        FileTree.RevealSearchFile(file);
        bool selectionChanged = !ReferenceEquals(file, SelectedChange);
        if (selectionChanged)
        {
            bool wasSuppressed = _suppressSelectionLoad;
            _suppressSelectionLoad = true;
            try { SelectSingleChange(file); }
            finally { _suppressSelectionLoad = wasSuppressed; }
        }

        if (match.IsDiffRow && file.IsConflicted)
        {
            IsFullFileMode = false;
            await LoadDiffAsync(file, compareConflictToHead: true);
        }
        else if (match.IsDiffRow && IsFullFileMode)
        {
            await ShowDiffAsync();
        }
        else if (selectionChanged || IsDiffLoading || DiffSearch.Selection is null)
        {
            await LoadSelectedChangeAsync(file);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(file, SelectedChange)) { return null; }
        DiffLine? line = DiffSearchViewModel.FindLine(DiffLines, file, match, DiffSearch.Query);
        if (line is null && !match.IsDiffRow && !IsFullFileMode)
        {
            await ShowFullFileAsync(file);
            cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(file, SelectedChange))
            {
                line = DiffSearchViewModel.FindLine(DiffLines, file, match, DiffSearch.Query);
            }
        }

        return line;
    }

    private bool MatchesChangeFilter(GitChangedFile file, string pathFilter) => DiffSearch.IsFiltering
        ? DiffSearch.Includes(null, file)
        : MatchesFileFilter(file, pathFilter);
}
