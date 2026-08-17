using System.Collections.Generic;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string primaryButtonText);

    Task<int?> ShowCherryPickMainlineDialogAsync(GitCommit commit);

    Task<bool> ConfirmCherryPickAsync(
        string branchName,
        IReadOnlyList<GitCommit> commits,
        GitCherryPickOptions options);

    Task<string?> ShowTextInputAsync(TextInputDialogRequest request);

    Task<CommitDialogResult?> ShowCommitDialogAsync(CommitDialogRequest request);

    Task<BranchCreationRequest?> ShowCreateBranchDialogAsync(RepositoryInfo repository);

    Task<string?> ShowRenameBranchDialogAsync(GitBranch branch);

    Task<string?> ShowBranchDescriptionDialogAsync(GitBranch branch);

    Task<TagCreationRequest?> ShowCreateTagDialogAsync(IReadOnlyList<GitCommit> commits);

    Task<TagConflictResolution> ShowTagConflictDialogAsync(string tagName, string localHash, string remoteHash, string remoteName);

    Task<WorktreeCreationRequest?> ShowCreateWorktreeDialogAsync(
        RepositoryInfo repository,
        string path,
        string startPoint,
        string newBranchName = "",
        WorktreeCreationMode creationMode = WorktreeCreationMode.ExistingBranch,
        bool canUseExistingBranch = true,
        GitRevisionKind startPointKind = GitRevisionKind.Branch);

    Task<GitArchiveDialogResult?> ShowArchiveDialogAsync(RepositoryInfo repository);
}
