using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.ViewModels;

public sealed partial class HistoryViewModel
{
    private async Task<bool> VerifyHistoryTargetAsync(RepositoryInfo repository, string expectedHead, string expectedBranch)
    {
        if (!ReferenceEquals(repository, _mainWindowViewModel.CurrentRepository)) { return false; }
        GitCommit head = await _gitService.GetLastCommitAsync(repository);
        GitCommandResult branch = await _gitService.Editor.RunAsync(repository, ["rev-parse", "--symbolic-full-name", "HEAD"]);
        if (!ReferenceEquals(repository, _mainWindowViewModel.CurrentRepository)) { return false; }
        GitOperationState state = await _gitService.GetOperationStateAsync(repository);
        if (!ReferenceEquals(repository, _mainWindowViewModel.CurrentRepository)) { return false; }
        if (!branch.IsSuccess || head.Hash != expectedHead || branch.StandardOutput.Trim() != expectedBranch
            || state.Kind != GitOperationKind.None)
        {
            ShowNotification(AppNotificationSeverity.Warning, _localizationService.GetString("HistoryEditHistoryChanged"));
            return false;
        }
        return true;
    }

    private async Task ExecuteHistoryRebaseAsync(RepositoryInfo repository, string expectedHead, string expectedBranch, GitCommit basis,
        Func<string, string> buildPlan, IReadOnlyDictionary<string, string>? messages, bool preserveMerges,
        Func<bool>? isCurrent = null, Action? beforeRefresh = null)
    {
        try
        {
            if (isCurrent?.Invoke() == false || !await VerifyHistoryTargetAsync(repository, expectedHead, expectedBranch)) { return; }
            List<string> arguments = ["rebase", "--interactive", "--force-rebase", "--reapply-cherry-picks", "--empty=keep", "--keep-empty",
                "--no-autosquash", "--no-update-refs", preserveMerges ? "--rebase-merges" : "--no-rebase-merges", "--no-fork-point"];
            arguments.Add(basis.ParentHashes.Count == 0 ? "--root" : basis.ParentHashes[0]);
            string? planError = null;
            bool planAccepted = false;
            ProgressMessage = _localizationService.GetString("HistoryEditRunning");
            GitCommandResult result = await _gitService.Editor.RunAsync(repository, arguments, (sequence, text) =>
            {
                if (!ReferenceEquals(repository, _mainWindowViewModel.CurrentRepository) || isCurrent?.Invoke() == false) { return Task.FromResult<string?>(null); }
                if (sequence)
                {
                    try { string plan = buildPlan(text); planAccepted = true; return Task.FromResult<string?>(plan); }
                    catch (InvalidOperationException exception) { planError = exception.Message; return Task.FromResult<string?>(null); }
                }
                return Task.FromResult<string?>(text);
            }, preparedMessages: messages);
            if (!ReferenceEquals(repository, _mainWindowViewModel.CurrentRepository)) { return; }
            // Refresh before the conflict banner so that its navigation action survives.
            if (result.IsSuccess || planAccepted)
            {
                beforeRefresh?.Invoke();
                await RefreshHistoryCoreAsync();
            }
            if (!result.IsSuccess)
            {
                GitCommandException exception = new(result.CombinedOutput, result.ExitCode);
                if (!await _mainWindowViewModel.TryShowConflictWarningAsync(repository, this, exception))
                {
                    ShowError(_localizationService.GetString(planError is null ? "GitDangerousOperationFailed" : "HistoryEditPlanFailed"), planError ?? result.CombinedOutput);
                }
            }
            else { ShowSuccess(_localizationService.GetString("HistoryEditCompleted")); }
        }
        catch (Exception exception) when (exception is GitCommandException or System.IO.IOException)
        {
            if (!ReferenceEquals(repository, _mainWindowViewModel.CurrentRepository)) { return; }
            ShowError(_localizationService.GetString("GitDangerousOperationFailed"), exception.Message);
        }
        finally { ProgressMessage = ""; }
    }

}
