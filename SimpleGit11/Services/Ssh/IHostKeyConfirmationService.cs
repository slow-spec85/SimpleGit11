using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Services;

public sealed record HostKeyConfirmation(
    string Host,
    int Port,
    IReadOnlyList<string> Fingerprints);

public interface IHostKeyConfirmationService
{
    Task<bool> ConfirmAsync(
        HostKeyConfirmation confirmation,
        CancellationToken cancellationToken = default);
}
