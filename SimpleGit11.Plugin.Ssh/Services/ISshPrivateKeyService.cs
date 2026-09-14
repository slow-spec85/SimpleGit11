using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Plugin.Ssh.Services;

internal interface ISshPrivateKeyService
{
    Task GenerateAsync(
        string path,
        string? passphrase,
        CancellationToken cancellationToken = default);

    Task<bool> RequiresPassphraseAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<bool> CanOpenAsync(
        string path,
        string passphrase,
        CancellationToken cancellationToken = default);

    Task<string> GetAuthorizedKeyAsync(
        string path,
        string? passphrase,
        CancellationToken cancellationToken = default);
}
