using System;
using System.Threading;

namespace SimpleGit11.Plugin.Ssh.Services;

public sealed class SshConnectionMonitor
{
    private int _wasReported;

    public event EventHandler<Exception>? ConnectionLost;

    public void Report(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Interlocked.Exchange(ref _wasReported, 1) == 0)
        {
            ConnectionLost?.Invoke(this, exception);
        }
    }
}
