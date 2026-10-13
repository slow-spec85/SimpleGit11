using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Presentation.Commits;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.ViewModels;

public sealed partial class HistoryViewModel
{
    private HistoryEditSession? _historyDraft;
    private RepositoryInfo? _draftRepository;
    private IReadOnlyList<GitCommit> _historyBeforeEditing = [];
    private bool _graphBeforeEditing;
    private IReadOnlyList<GitCommit> _selectionBeforeEditing = [];
    private GitCommit? _draftBasis;
    private string _draftHead = "";
    private string _draftBranch = "";

    public override string CommitsTitle => _historyDraft is null
        ? base.CommitsTitle
        : string.Format(_localizationService.GetString("HistoryEditingCommitsTitle"),
            PluralizationService.FormatCommitCount(_historyDraft.Original.Count, _localizationService));

    public override ListViewSelectionMode CommitSelectionMode => IsHistoryEditing
        ? ListViewSelectionMode.Single : ListViewSelectionMode.Extended;

    public override bool IsHistoryEditing => _historyDraft is not null;
    public override bool IsCommitSearchEnabled => !IsHistoryEditing;
    public override bool IsCommitContentUnavailable => IsHistoryEditing && SelectedCommitRow?.IsDraftContentUnavailable == true;
    protected override bool CanLoadCommitContent => !IsCommitContentUnavailable;
    private bool CanStartHistoryEdit() => SelectedCommit is not null && !IsHistoryEditing && !IsGitOperationRunning && !IsRefreshing;
    private bool CanChangeDraft() => IsHistoryEditing && !IsGitOperationRunning;
    private bool CanChangeDraftCommit() => CanChangeDraft() && SelectedCommit is not null && _historyDraft!.IsActive(SelectedCommit.Hash);
    private bool CanDeleteDraftCommit() => CanChangeDraft() && SelectedCommit is not null && _historyDraft!.Original.Any(commit => commit.Hash == SelectedCommit.Hash)
        && (_historyDraft.IsActive(SelectedCommit.Hash) || _historyDraft.IsDeleted(SelectedCommit.Hash));
    private bool CanMergeDraftCommit() => CanChangeDraftCommit() && (_historyDraft!.Previous(SelectedCommit!.Hash) is not null
        || _historyDraft.MergeBoundary(SelectedCommit.Hash)?.ParentHashes.Any(_historyDraft.IsActive) == true);
    private bool CanMoveDraftUp() => CanChangeDraftCommit() && _historyDraft!.CanMove(SelectedCommit!.Hash, -1);
    private bool CanMoveDraftDown() => CanChangeDraftCommit() && _historyDraft!.CanMove(SelectedCommit!.Hash, 1);
    private bool CanUndoDraft() => CanChangeDraft() && _historyDraft!.CanUndo;
    private bool CanApplyHistoryEdit() => CanChangeDraft() && _historyDraft!.HasChanges;
    public string DeleteDraftCaption => _localizationService.GetString(SelectedCommit is not null && _historyDraft?.IsDeleted(SelectedCommit.Hash) == true ? "HistoryEditRestore" : "HistoryEditDelete");
    public string DeleteDraftGlyph => SelectedCommit is not null && _historyDraft?.IsDeleted(SelectedCommit.Hash) == true ? "\uE777" : "\uE74D";
    public string MergeDraftTooltip
    {
        get
        {
            GitCommit? target = SelectedCommit is null ? null : _historyDraft?.Previous(SelectedCommit.Hash);
            return target is null ? _localizationService.GetString("HistoryEditMerge")
                : string.Format(_localizationService.GetString("HistoryEditMergeHint"), target.ShortHash, _historyDraft!.Message(target.Hash).ReplaceLineEndings("\n").Split('\n')[0]);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartHistoryEdit))]
    private Task OnStartHistoryEditAsync() => _asyncCommandExecutor.ExecuteAsync(async () =>
    {
        GitCommit? selected = SelectedCommit;
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        if (selected is null || repository is null || !CanStartHistoryEdit()) { return; }
        await RunGitOperationAsync(async () =>
        {
            try
            {
                if ((await _gitService.GetOperationStateAsync(repository)).Kind != GitOperationKind.None)
                {
                    ShowNotification(AppNotificationSeverity.Warning, _localizationService.GetString("HistoryEditOperationInProgress"));
                    return;
                }
                GitCommit head = await _gitService.GetLastCommitAsync(repository);
                if (AllCommits.FirstOrDefault()?.Hash != head.Hash)
                {
                    await RefreshHistoryCoreAsync();
                    ShowNotification(AppNotificationSeverity.Warning, _localizationService.GetString("HistoryEditHistoryChanged"));
                    return;
                }
                GitCommit[] selection = SelectedCommits.Count > 1 ? SelectedCommits.ToArray() : [selected];
                GitCommit[] displayed = AllCommits.ToArray();
                int first = Array.FindIndex(displayed, commit => selection.Any(item => item.Hash == commit.Hash));
                int last = Array.FindLastIndex(displayed, commit => selection.Any(item => item.Hash == commit.Hash));
                if (first < 0 || last < 0) { return; }
                GitCommit basis = displayed[last];
                GitCommandResult ancestry = await _gitService.Editor.RunAsync(repository, ["merge-base", "--is-ancestor", basis.Hash, head.Hash]);
                if (!ancestry.IsSuccess)
                {
                    ShowNotification(AppNotificationSeverity.Warning, _localizationService.GetString("HistoryQuickNotAncestor"));
                    return;
                }
                List<string> revisions = ["rev-list", "--topo-order", head.Hash];
                if (basis.ParentHashes.Count > 0) { revisions.Add("^" + basis.ParentHashes[0]); }
                string[] hashes = (await ReadQuickGitAsync(repository, revisions)).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                List<GitCommit> range = [];
                foreach (string hash in hashes) { range.Add(await ReadQuickCommitAsync(repository, hash)); }
                IReadOnlySet<string>? editable = selection.Length > 1
                    ? displayed.Skip(first).Take(last - first + 1).Select(commit => commit.Hash).ToHashSet() : null;
                if (!range.Any(commit => commit.Hash == basis.Hash))
                {
                    ShowNotification(AppNotificationSeverity.Warning, _localizationService.GetString("HistoryEditHistoryChanged"));
                    return;
                }
                GitCommandResult branch = await _gitService.Editor.RunAsync(repository, ["rev-parse", "--symbolic-full-name", "HEAD"]);
                if (!branch.IsSuccess) { throw new GitCommandException(branch.CombinedOutput, branch.ExitCode); }
                if (!ReferenceEquals(repository, _mainWindowViewModel.CurrentRepository)) { return; }
                _historyBeforeEditing = AllCommits.ToArray();
                _selectionBeforeEditing = selection;
                _graphBeforeEditing = IsCommitGraphVisible;
                ResetCommitFiltersCommand.Execute(null);
                _draftBasis = basis;
                _draftHead = head.Hash;
                _draftBranch = branch.StandardOutput.Trim();
                _draftRepository = repository;
                _historyDraft = new HistoryEditSession(range, editable);
                IsCommitGraphVisible = false;
                RenderHistoryDraft(selected.Hash);
            }
            catch (Exception exception) when (exception is GitCommandException or System.IO.IOException)
            {
                ShowError(_localizationService.GetString("GitLogCommandFailed"), exception.Message);
            }
        });
    });

    [RelayCommand(CanExecute = nameof(CanMoveDraftUp))]
    private Task OnMoveDraftUpAsync() => _asyncCommandExecutor.ExecuteAsync(() => MoveDraftAsync(-1));

    [RelayCommand(CanExecute = nameof(CanMoveDraftDown))]
    private Task OnMoveDraftDownAsync() => _asyncCommandExecutor.ExecuteAsync(() => MoveDraftAsync(1));

    private async Task MoveDraftAsync(int direction)
    {
        HistoryEditSession? draft = _historyDraft;
        string? hash = SelectedCommit?.Hash;
        if (draft is null || hash is null || !CanChangeDraftCommit()) { return; }
        int revision = draft.Revision;
        GitCommit? boundary = draft.MoveBoundary(hash, direction);
        int? parent = boundary is null ? null : await _dialogService.ShowHistoryParentDialogAsync(boundary);
        if (boundary is not null && parent is null || !ReferenceEquals(draft, _historyDraft) || draft.Revision != revision || !CanChangeDraft() || !draft.CanMove(hash, direction)) { return; }
        if (parent is int chosen && !draft.CanMoveToParent(hash, direction, chosen))
        {
            ShowNotification(AppNotificationSeverity.Warning, _localizationService.GetString("HistoryEditParentOutsideRange"));
            return;
        }
        try { draft.Move(hash, direction, parent); }
        catch (InvalidOperationException)
        {
            ShowNotification(AppNotificationSeverity.Warning, _localizationService.GetString("HistoryEditInvalidMove"));
            return;
        }
        RenderHistoryDraft(hash);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteDraftCommit))]
    private void OnDeleteDraftCommit()
    {
        string hash = SelectedCommit!.Hash;
        _historyDraft!.SetDeleted(hash, !_historyDraft.IsDeleted(hash));
        RenderHistoryDraft(hash);
    }

    [RelayCommand(CanExecute = nameof(CanMergeDraftCommit))]
    private Task OnMergeDraftWithMessageAsync() => _asyncCommandExecutor.ExecuteAsync(async () =>
    {
        HistoryEditSession? draft = _historyDraft;
        string? hash = SelectedCommit?.Hash;
        if (draft is null || hash is null || !CanChangeDraftCommit()) { return; }
        int revision = draft.Revision;
        GitCommit? boundary = draft.MergeBoundary(hash);
        int? parent = boundary is null ? null : await _dialogService.ShowHistoryParentDialogAsync(boundary);
        if (boundary is not null && parent is null || !ReferenceEquals(draft, _historyDraft) || draft.Revision != revision || !CanChangeDraft()) { return; }
        GitCommit? target = draft.MergeTarget(hash, parent);
        if (target is null || !draft.IsActive(target.Hash))
        {
            ShowNotification(AppNotificationSeverity.Warning, _localizationService.GetString("HistoryEditParentOutsideRange"));
            return;
        }
        string initial = draft.Message(target.Hash) + "\n\n" + draft.Message(hash!);
        CommitDialogResult? answer = await _dialogService.ShowCommitDialogAsync(CommitDialogRequest.CreateMessageEdit(initial, []));
        if (answer is null || string.IsNullOrWhiteSpace(answer.Message) || !ReferenceEquals(draft, _historyDraft)
            || draft.Revision != revision || !CanChangeDraft() || draft.MergeTarget(hash!, parent)?.Hash != target.Hash) { return; }
        draft.Merge(hash!, answer.Message.Trim(), parent);
        RenderHistoryDraft(target.Hash);
    });

    [RelayCommand(CanExecute = nameof(CanUndoDraft))]
    private void OnUndoHistoryEdit() { string? hash = SelectedCommit?.Hash; _historyDraft!.Undo(); RenderHistoryDraft(hash); }

    [RelayCommand(CanExecute = nameof(CanChangeDraft))]
    private void OnCancelHistoryEdit() => CloseHistoryDraft();

    private async Task EditDraftMessageAsync()
    {
        HistoryEditSession? draft = _historyDraft;
        string? hash = SelectedCommit?.Hash;
        if (draft is null || hash is null || !CanChangeDraftCommit()) { return; }
        int revision = draft.Revision;
        CommitDialogResult? answer = await _dialogService.ShowCommitDialogAsync(CommitDialogRequest.CreateMessageEdit(draft.Message(hash), []));
        if (answer is null || string.IsNullOrWhiteSpace(answer.Message) || !ReferenceEquals(draft, _historyDraft) || draft.Revision != revision || !CanChangeDraft() || !draft.IsActive(hash)) { return; }
        draft.SetMessage(hash, answer.Message.Trim());
        RenderHistoryDraft(hash);
    }

    [RelayCommand(CanExecute = nameof(CanApplyHistoryEdit))]
    private Task OnApplyHistoryEditAsync() => _asyncCommandExecutor.ExecuteAsync(async () =>
    {
        HistoryEditSession? draft = _historyDraft;
        RepositoryInfo? repository = _draftRepository;
        if (draft is null || repository is null) { return; }
        int revision = draft.Revision;
        string warning = _localizationService.GetString("HistoryEditApplyWarning");
        if (draft.Original.Any(commit => commit.IsSynchronized == true)) { warning += "\n\n" + _localizationService.GetString("HistoryEditPublishedWarning"); }
        if (!await _dialogService.ConfirmAsync(_localizationService.GetString("HistoryEditTitle"), warning, _localizationService.GetString("HistoryEditApply"))
            || !ReferenceEquals(draft, _historyDraft) || draft.Revision != revision || !ReferenceEquals(repository, _mainWindowViewModel.CurrentRepository)) { return; }
        await RunGitOperationAsync(() => ExecuteHistoryRebaseAsync(repository, _draftHead, _draftBranch, _draftBasis!,
            draft.BuildPlan, draft.PreparedMessages, preserveMerges: draft.PreservesMerges,
            isCurrent: () => ReferenceEquals(draft, _historyDraft), beforeRefresh: CloseHistoryDraft));
    });

    private void RenderHistoryDraft(string? selectedHash)
    {
        if (_historyDraft is null) { return; }
        List<CommitBrowserRowViewItem> rows = _historyDraft.CreateRows(_localizationService).ToList();
        CommitRows.Clear();
        Commits.Clear();
        foreach (CommitBrowserRowViewItem row in rows) { CommitRows.Add(row); Commits.Add(row.Commit); }
        SelectedCommitRow = rows.FirstOrDefault(row => row.Commit.Hash == selectedHash && row.IsSelectable) ?? rows.FirstOrDefault(row => row.IsSelectable);
        NotifyHistoryEditState();
        RefreshCommitContent();
    }

    private void CloseHistoryDraft()
    {
        if (_historyDraft is null) { return; }
        string? selectedHash = SelectedCommit?.Hash;
        _historyDraft = null;
        _draftRepository = null;
        _draftBasis = null;
        ReplaceCommits(_historyBeforeEditing);
        _historyBeforeEditing = [];
        IsCommitGraphVisible = _graphBeforeEditing;
        SelectedCommit = Commits.FirstOrDefault(commit => commit.Hash == selectedHash) ?? Commits.FirstOrDefault();
        SetSelectedCommits(_selectionBeforeEditing.Where(commit => Commits.Any(item => item.Hash == commit.Hash)));
        _selectionBeforeEditing = [];
        NotifyHistoryEditState();
        RefreshCommitContent();
    }

    private void NotifyHistoryEditState()
    {
        foreach (string property in new[] { nameof(CommitsTitle), nameof(CommitSelectionMode), nameof(IsHistoryEditing), nameof(IsCommitSearchEnabled), nameof(IsCommitContentUnavailable), nameof(IsCommitContentAvailable),
            nameof(IsLoadMoreHistoryVisible), nameof(SelectedCommitMessage), nameof(DeleteDraftCaption), nameof(DeleteDraftGlyph), nameof(MergeDraftTooltip) }) { OnPropertyChanged(property); }
        StartHistoryEditCommand.NotifyCanExecuteChanged();
        MoveDraftUpCommand.NotifyCanExecuteChanged();
        MoveDraftDownCommand.NotifyCanExecuteChanged();
        DeleteDraftCommitCommand.NotifyCanExecuteChanged();
        MergeDraftWithMessageCommand.NotifyCanExecuteChanged();
        UndoHistoryEditCommand.NotifyCanExecuteChanged();
        ApplyHistoryEditCommand.NotifyCanExecuteChanged();
        CancelHistoryEditCommand.NotifyCanExecuteChanged();
        ToggleCommitGraphCommand.NotifyCanExecuteChanged();
        FilterByChangedFileCommand.NotifyCanExecuteChanged();
        NavigateToParentCommitCommand.NotifyCanExecuteChanged();
        UpdateCommandStates();
    }
}
