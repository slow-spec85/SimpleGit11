using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Services.Execution;

public interface IRemoteSshIdentityStore
{
    Task<IReadOnlyList<SshIdentity>> GetIdentitiesAsync(
        CancellationToken cancellationToken = default);

    Task<SshIdentity> CreateIdentityAsync(
        string host,
        int port,
        string? user,
        string machineName,
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

public sealed record SshIdentity(
    string PrivateKeyPath,
    string PublicKey,
    string Fingerprint,
    bool IsConfigured = false);
