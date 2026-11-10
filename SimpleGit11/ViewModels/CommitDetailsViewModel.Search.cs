using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.ViewModels;

public abstract partial class CommitDetailsViewModel
{
    private bool _navigatingSearchResult;
    private string? _branchHistoryRevision;

    public DiffSearchViewModel DiffSearch { get; }

    protected void SetBranchHistoryRevision(string? revision)
    {
        _branchHistoryRevision = revision;
        DiffSearch.CanSearchHistory = revision is not null;
    }

    protected virtual Task<GitCommit> ResolveSearchCommitAsync(GitCommit commit, CancellationToken cancellationToken) =>
        Task.FromResult(commit);

    protected virtual void SelectSearchCommit(GitCommit commit) => SelectedCommit = commit;

    protected virtual void ApplyCommitSearchFilter() { }

    protected virtual Task LoadRemainingSearchCommitsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void FilterSearchResults()
    {
        bool wasNavigating = _navigatingSearchResult;
        _navigatingSearchResult |= DiffSearch.IsFiltering;
        try
        {
            ApplyCommitSearchFilter();
            ApplyChangedFilesFilter();
        }
        finally { _navigatingSearchResult = wasNavigating; }
    }

    private async Task<IReadOnlyList<GitFileSearchResult>> SearchChangedFilesAsync(
        string query, bool changedLinesOnly, bool searchHistory, CancellationToken cancellationToken)
    {
        if (_mainWindowViewModel.CurrentRepository is not RepositoryInfo repository || !CanLoadCommitContent)
        {
            return [];
        }

        if (searchHistory)
        {
            if (_branchHistoryRevision is not string revision) { return []; }
            await LoadRemainingSearchCommitsAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(repository, _mainWindowViewModel.CurrentRepository)) { return []; }
            return await _gitService.Diff.SearchHistoryAsync(
                repository, query, changedLinesOnly, cancellationToken, revision);
        }

        return SelectedCommit is GitCommit commit
            ? await _gitService.Diff.SearchFilesAsync(repository, commit, _allChangedFiles.ToArray(),
                query, changedLinesOnly, cancellationToken, SelectedChangedFile)
            : [];
    }

    private async Task<DiffLine?> ShowSearchMatchAsync(
        GitFileSearchResult result, GitFileSearchMatch match, CancellationToken cancellationToken)
    {
        if (result.Commit is not GitCommit resultCommit || !CanLoadCommitContent) { return null; }

        GitCommit? commit = SelectedCommit;
        bool commitChanged = commit?.Hash != resultCommit.Hash;
        if (commitChanged || commit is null || _allChangedFiles.Count == 0)
        {
            _navigatingSearchResult = true;
            try
            {
                if (commitChanged || commit is null)
                {
                    commit = await ResolveSearchCommitAsync(resultCommit, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    SelectSearchCommit(commit);
                }
                await LoadCommitDetailsAsync(commit);
            }
            finally { _navigatingSearchResult = false; }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(SelectedCommit, commit)) { return null; }
        GitChangedFile? file = ChangedFiles.FirstOrDefault(change => change.Path == result.File.Path);
        if (file is null) { return null; }

        FileTree.RevealSearchFile(file);
        bool selectionChanged = !ReferenceEquals(file, SelectedChangedFile);
        if (selectionChanged)
        {
            _navigatingSearchResult = true;
            try { SelectedChangedFile = file; }
            finally { _navigatingSearchResult = false; }
        }

        if (match.IsDiffRow && IsFullFileMode)
        {
            await ShowDiffCoreAsync();
        }
        else if (commitChanged || selectionChanged || IsDiffLoading || DiffSearch.Selection is null)
        {
            await LoadSelectedFileAsync(commit, file);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(SelectedChangedFile, file)) { return null; }
        DiffLine? line = DiffSearchViewModel.FindLine(DiffLines, file, match, DiffSearch.Query);
        if (line is null && !match.IsDiffRow && !IsFullFileMode)
        {
            await ShowFullFileCoreAsync(commit, file);
            cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(SelectedChangedFile, file))
            {
                line = DiffSearchViewModel.FindLine(DiffLines, file, match, DiffSearch.Query);
            }
        }

        return line;
    }
}
