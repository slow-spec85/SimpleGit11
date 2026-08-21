using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.Presentation.Services;

public sealed class GitCommitWorkflowService : IGitCommitWorkflowService
{
    private readonly IGitCommitService _commitService;
    private readonly IDialogService _dialogService;
    private readonly ILocalizationService _localizationService;

    public GitCommitWorkflowService(
        IGitCommitService commitService,
        IDialogService dialogService,
        ILocalizationService localizationService)
    {
        _commitService = commitService;
        _dialogService = dialogService;
        _localizationService = localizationService;
    }

    public Task<GitCommitOperationResult> CreateAsync(
        RepositoryInfo repository,
        string message)
    {
        return ExecuteAsync(repository, message, amend: false, checkForEmptyCommit: true);
    }

    public Task<GitCommitOperationResult> AmendAsync(
        RepositoryInfo repository,
        string? message)
    {
        return ExecuteAsync(repository, message, amend: true, checkForEmptyCommit: true);
    }

    public Task<GitCommitOperationResult> CompleteMergeAsync(
        RepositoryInfo repository,
        string message)
    {
        return ExecuteAsync(repository, message, amend: false, checkForEmptyCommit: false);
    }

    private async Task<GitCommitOperationResult> ExecuteAsync(
        RepositoryInfo repository,
        string? message,
        bool amend,
        bool checkForEmptyCommit)
    {
        GitCommitOptions options = GitCommitOptions.Default;
        if (checkForEmptyCommit
            && await _commitService.WouldCreateEmptyCommitAsync(repository, amend))
        {
            bool confirmed = await _dialogService.ConfirmAsync(
                _localizationService.GetString(amend
                    ? "EmptyAmendDialogTitle"
                    : "EmptyCommitDialogTitle"),
                _localizationService.GetString(amend
                    ? "EmptyAmendDialogMessage"
                    : "EmptyCommitDialogMessage"),
                _localizationService.GetString(amend
                    ? "EmptyAmendDialogPrimaryButton"
                    : "EmptyCommitDialogPrimaryButton"));
            if (!confirmed)
            {
                return GitCommitOperationResult.Canceled;
            }

            options = new GitCommitOptions(AllowEmpty: true);
        }

        string output = amend
            ? await _commitService.AmendAsync(repository, message, options)
            : await _commitService.CommitAsync(repository, message!, options);
        return GitCommitOperationResult.Succeeded(output);
    }
}
