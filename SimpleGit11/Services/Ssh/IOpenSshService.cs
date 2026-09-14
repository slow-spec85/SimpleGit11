using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Services;

public interface IOpenSshService
{
    Task<IReadOnlyList<SshIdentity>> GetIdentitiesAsync(
        CancellationToken cancellationToken = default);

    Task EnsureTrustedAsync(string remoteUrl, CancellationToken cancellationToken = default);

    Task<SshIdentity> CreateIdentityAsync(
        string remoteUrl,
        string passphrase,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetIdentityConfigurationReferencesAsync(
        string privateKeyPath,
        CancellationToken cancellationToken = default);

    Task DeleteIdentityAsync(
        string privateKeyPath,
        bool removeExternalReferences,
        CancellationToken cancellationToken = default);
}
