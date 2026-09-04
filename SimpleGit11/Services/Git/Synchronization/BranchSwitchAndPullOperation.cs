using System;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

internal static class BranchSwitchAndPullOperation
{
    public static async Task RunWhenConfirmedAsync(
        Func<Task<bool>> confirmAsync,
        Func<Task> operationAsync)
    {
        if (await confirmAsync())
        {
            await operationAsync();
        }
    }

    public static async Task<GitRemoteOperationResult> ExecuteAsync(
        IGitBranchService branchService,
        IGitRemoteService remoteService,
        RepositoryInfo repository,
        BranchSynchronizationItem branch,
        GitRemote defaultRemote,
        Action<string> onBranchSwitched,
        CancellationToken cancellationToken)
    {
        await branchService.SwitchAsync(repository, branch.Name);
        onBranchSwitched(branch.Name);

        return branch.HasUpstream
            ? await remoteService.PullAsync(repository, cancellationToken)
            : await remoteService.PullAsync(
                repository,
                defaultRemote.Name,
                branch.Name,
                cancellationToken);
    }
}
