using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.ViewModels;

public sealed partial class HistoryViewModel
{
    private bool CanQuickHistoryChange() => CanRunHistoryOperation() && !IsRefreshing;
    private bool CanQuickMergeCommit() => CanQuickHistoryChange() && SelectedCommit!.ParentHashes.Count > 0;

    [RelayCommand(CanExecute = nameof(CanQuickMergeCommit))]
    private Task OnMergeCommitWithPreviousAsync() => _asyncCommandExecutor.ExecuteAsync(() => RunQuickHistoryChangeAsync("fixup"));

    [RelayCommand(CanExecute = nameof(CanQuickHistoryChange))]
    private Task OnDeleteHistoryCommitAsync() => _asyncCommandExecutor.ExecuteAsync(() => RunQuickHistoryChangeAsync("drop"));

    private async Task RunQuickHistoryChangeAsync(string action)
    {
        GitCommit? selected = SelectedCommit;
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        if (selected is null || repository is null || !CanQuickHistoryChange()) { return; }
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
                if (head.Hash != AllCommits.FirstOrDefault()?.Hash)
                {
                    ShowNotification(AppNotificationSeverity.Warning, _localizationService.GetString("HistoryEditHistoryChanged"));
                    return;
                }
                string branch = (await ReadQuickGitAsync(repository, ["rev-parse", "--symbolic-full-name", "HEAD"])).Trim();
                GitCommandResult ancestry = await _gitService.Editor.RunAsync(repository, ["merge-base", "--is-ancestor", selected.Hash, head.Hash]);
                if (!ancestry.IsSuccess)
                {
                    ShowNotification(AppNotificationSeverity.Warning, _localizationService.GetString("HistoryQuickNotAncestor"));
                    return;
                }
                GitCommit? previous = action == "fixup" && selected.ParentHashes.Count > 0
                    ? await ReadQuickCommitAsync(repository, selected.ParentHashes[0]) : null;
                if (action == "fixup" && previous is null) { return; }
                if (!ReferenceEquals(repository, _mainWindowViewModel.CurrentRepository)) { return; }
                string? message = null;
                if (action != "drop")
                {
                    string initial = previous is null ? selected.Message : previous.Message + "\n\n" + selected.Message;
                    IReadOnlyList<GitChangedFile> files = selected.ChangedFilePaths.Concat(previous?.ChangedFilePaths ?? [])
                        .Distinct().Select(path => new GitChangedFile(path, "Changed")).ToArray();
                    CommitDialogResult? answer = await _dialogService.ShowCommitDialogAsync(CommitDialogRequest.CreateMessageEdit(initial, files));
                    message = answer?.Message?.Trim();
                    if (string.IsNullOrWhiteSpace(message) || action == "reword" && message == selected.Message) { return; }
                }
                bool published = selected.IsSynchronized == true || previous?.IsSynchronized == true;
                if (action == "drop" || published)
                {
                    string warning = action == "drop"
                        ? string.Format(_localizationService.GetString("HistoryQuickDeleteWarning"), selected.ShortHash, selected.Title)
                        : string.Format(_localizationService.GetString(action == "fixup" ? "HistoryQuickMergeWarning" : "HistoryQuickMessageWarning"),
                            selected.ShortHash, selected.Title, previous?.ShortHash, previous?.Title);
                    if (published) { warning += "\n\n" + _localizationService.GetString("HistoryEditPublishedWarning"); }
                    if (!await _dialogService.ConfirmAsync(_localizationService.GetString("HistoryEditTitle"), warning,
                        _localizationService.GetString(action == "drop" ? "HistoryEditDelete" : "HistoryEditApply"))) { return; }
                }
                if (!ReferenceEquals(repository, _mainWindowViewModel.CurrentRepository)) { return; }
                if (action == "reword" && selected.Hash == head.Hash)
                {
                    if (!await VerifyHistoryTargetAsync(repository, head.Hash, branch)) { return; }
                    ProgressMessage = _localizationService.GetString("AmendingCommitMessageProgress");
                    await _gitService.Commits.AmendAsync(repository, message, new GitCommitOptions(AllowEmpty: true, MessageOnly: true));
                    if (!ReferenceEquals(repository, _mainWindowViewModel.CurrentRepository)) { return; }
                    await RefreshHistoryCoreAsync();
                    ShowSuccess(_localizationService.GetString("CommitMessageAmended"));
                    return;
                }
                GitCommit basis = previous ?? selected;
                IReadOnlyDictionary<string, string>? messages = message is null ? null
                    : new Dictionary<string, string> { [basis.Hash] = message.ReplaceLineEndings("\n") + "\n" };
                await ExecuteHistoryRebaseAsync(repository, head.Hash, branch, basis,
                    text => BuildQuickHistoryPlan(text, selected.Hash, action, previous?.Hash), messages, preserveMerges: true);
            }
            catch (Exception exception) when (exception is GitCommandException or IOException)
            {
                if (!ReferenceEquals(repository, _mainWindowViewModel.CurrentRepository)) { return; }
                if (exception is not GitCommandException gitException || !await _mainWindowViewModel.TryShowConflictWarningAsync(repository, this, gitException))
                {
                    ShowError(_localizationService.GetString("GitDangerousOperationFailed"), exception.Message);
                }
            }
            finally { ProgressMessage = ""; }
        });
    }

    private async Task<string> ReadQuickGitAsync(RepositoryInfo repository, IReadOnlyList<string> arguments)
    {
        GitCommandResult result = await _gitService.Editor.RunAsync(repository, arguments);
        if (!result.IsSuccess) { throw new GitCommandException(result.CombinedOutput, result.ExitCode); }
        return result.StandardOutput;
    }

    private async Task<GitCommit> ReadQuickCommitAsync(RepositoryInfo repository, string hash)
    {
        GitCommit? loaded = AllCommits.FirstOrDefault(commit => commit.Hash == hash);
        if (loaded is not null) { return loaded; }
        string message = (await ReadQuickGitAsync(repository, ["show", "--no-patch", "--format=%B", hash])).TrimEnd('\n');
        string[] parents = (await ReadQuickGitAsync(repository, ["rev-list", "--parents", "-n", "1", hash]))
            .Split([' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        bool published = !string.IsNullOrWhiteSpace(await ReadQuickGitAsync(repository,
            ["for-each-ref", "--contains=" + hash, "--format=%(refname)", "refs/remotes/"]));
        return new GitCommit(hash, hash[..Math.Min(7, hash.Length)], "", "", null,
            message.Split(['\r', '\n'], 2)[0], message, isSynchronized: published, parentHashes: parents.Skip(1).ToArray());
    }

    private static string BuildQuickHistoryPlan(string text, string hash, string action, string? previousHash)
    {
        List<string> steps = text.Split('\n').ToList();
        static string[] Parts(string line) => line.TrimStart().Split([' ', '\t'], 4, StringSplitOptions.RemoveEmptyEntries);
        static bool IsMerge(string line) => Parts(line).FirstOrDefault() is "merge" or "m";
        static string OriginalHash(string line)
        {
            string[] parts = Parts(line);
            if (parts.Length < 2) { return ""; }
            if (parts[0] is "pick" or "p" or "reword" or "r" or "edit" or "e" or "squash" or "s" or "drop" or "d") { return parts[1]; }
            if (parts[0] is "fixup" or "f") { return parts[1] is "-C" or "-c" ? parts.ElementAtOrDefault(2) ?? "" : parts[1]; }
            return parts.Length >= 3 && parts[0] is "merge" or "m" && parts[1] is "-C" or "-c" ? parts[2] : "";
        }
        int Find(string originalHash)
        {
            int[] matches = steps.Select((step, index) => (Hash: OriginalHash(step), Index: index))
                .Where(item => item.Hash.Length > 0 && originalHash.StartsWith(item.Hash, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Index).ToArray();
            return matches.Length == 1 ? matches[0] : throw new InvalidOperationException("The selected commit is missing or ambiguous in the Git todo plan.");
        }
        void Reword(int index, string originalHash)
        {
            if (IsMerge(steps[index]))
            {
                // Preserve all merge labels and parent references while changing its message.
                string[] parts = Parts(steps[index]);
                if (parts.Length < 4) { throw new InvalidOperationException("The Git merge instruction has no parent label."); }
                steps[index] = $"merge -c {originalHash} {parts[3]}";
            }
            else { steps[index] = "reword " + originalHash; }
        }
        int selectedIndex = Find(hash);
        if (action == "reword") { Reword(selectedIndex, hash); }
        else if (action == "drop") { steps[selectedIndex] = "drop " + hash; }
        else
        {
            int previousIndex = Find(previousHash!);
            if (previousIndex >= selectedIndex) { throw new InvalidOperationException("The previous commit does not precede the selected commit in the Git todo plan."); }
            Reword(previousIndex, previousHash!);
            if (IsMerge(steps[selectedIndex]))
            {
                // A merge cannot be used as a fixup patch. Replay its change
                // relative to the first parent, then amend the preceding commit.
                steps[selectedIndex] = "drop " + hash;
                steps.Insert(selectedIndex + 1, $"exec git cherry-pick --no-commit --mainline 1 {hash} && git commit --amend --no-edit --allow-empty");
            }
            else { steps[selectedIndex] = "fixup " + hash; }
        }
        return string.Join('\n', steps);
    }
}
