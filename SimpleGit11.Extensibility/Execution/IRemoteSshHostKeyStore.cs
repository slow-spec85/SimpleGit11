using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Services.Execution;

public interface IRemoteSshHostKeyStore
{
    Task<string> ReadKnownHostsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ScanHostKeysAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default);

    Task AppendKnownHostsAsync(
        IReadOnlyList<string> keyLines,
        CancellationToken cancellationToken = default);
}
