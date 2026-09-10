using System;
using System.Diagnostics;
using System.IO;
using SimpleGit11.Services;

namespace SimpleGit11.Presentation.Services;

public sealed class ApplicationInstanceLauncher : IApplicationInstanceLauncher
{
    public void OpenRepository(string repositoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The application executable path is unavailable.");
        ProcessStartInfo startInfo = CreateStartInfo(executablePath, repositoryPath);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The application process could not be started.");
    }

    internal static ProcessStartInfo CreateStartInfo(string executablePath, string repositoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(ApplicationLaunchArguments.OpenRepositoryOption);
        startInfo.ArgumentList.Add(Path.GetFullPath(repositoryPath));
        return startInfo;
    }
}
