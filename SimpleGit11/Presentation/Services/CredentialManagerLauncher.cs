using System;
using System.Diagnostics;
using SimpleGit11.Services;

namespace SimpleGit11.Presentation.Services;

public sealed class CredentialManagerLauncher : ICredentialManagerLauncher
{
    public void OpenCredentialManager()
    {
        ProcessStartInfo startInfo = CreateStartInfo();
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows Credential Manager could not be opened.");
    }

    internal static ProcessStartInfo CreateStartInfo()
    {
        ProcessStartInfo startInfo = new("control.exe")
        {
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add("/name");
        startInfo.ArgumentList.Add("Microsoft.CredentialManager");
        return startInfo;
    }
}
