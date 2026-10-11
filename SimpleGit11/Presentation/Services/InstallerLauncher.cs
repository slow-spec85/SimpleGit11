using System;
using System.Diagnostics;
using System.IO;
using SimpleGit11.Services;

namespace SimpleGit11.Presentation.Services;

public sealed class InstallerLauncher : IInstallerLauncher
{
    public void Launch(string installerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        string fullPath = Path.GetFullPath(installerPath);
        if (!File.Exists(fullPath)
            || !string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("The downloaded installer was not found.", fullPath);
        }

        ProcessStartInfo startInfo = new(fullPath)
        {
            UseShellExecute = true
        };
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The setup application could not be started.");
    }
}
