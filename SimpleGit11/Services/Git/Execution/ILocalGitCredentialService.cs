using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Services.Git.Execution;

public interface ILocalGitCredentialService
{
    Task<GitHttpAuthentication?> GetAsync(
        string remoteUrl,
        CancellationToken cancellationToken = default);

    Task ApproveAsync(
        GitHttpAuthentication credential,
        CancellationToken cancellationToken = default);

    Task RejectAsync(
        GitHttpAuthentication credential,
        CancellationToken cancellationToken = default);
}
