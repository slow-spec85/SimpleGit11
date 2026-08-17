using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SimpleGit11.Tests.Build;

[TestClass]
public sealed class PublishPathSafetyTests
{
    [TestMethod]
    public async Task PublicationPaths_RejectReparsePoints()
    {
        string repositoryRoot = FindRepositoryRoot();
        string testScript = Path.Combine(
            repositoryRoot,
            "SimpleGit11.Tests",
            "Build",
            "PublishPathSafety.Tests.ps1");
        string pathSafetyScript = Path.Combine(
            repositoryRoot,
            "SimpleGit11",
            "Build",
            "Publish-PathSafety.ps1");

        ProcessStartInfo startInfo = new("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(testScript);
        startInfo.ArgumentList.Add("-PathSafetyScript");
        startInfo.ArgumentList.Add(pathSafetyScript);

        using Process process = Process.Start(startInfo)
            ?? throw new AssertFailedException("Could not start Windows PowerShell.");
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();
        string standardOutput = await standardOutputTask;
        string standardError = await standardErrorTask;

        Assert.AreEqual(
            0,
            process.ExitCode,
            $"Publication path safety tests failed.{Environment.NewLine}" +
            $"STDOUT:{Environment.NewLine}{standardOutput}{Environment.NewLine}" +
            $"STDERR:{Environment.NewLine}{standardError}");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string projectPath = Path.Combine(directory.FullName, "SimpleGit11", "SimpleGit11.csproj");
            if (File.Exists(projectPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail($"Could not locate the repository root from {AppContext.BaseDirectory}.");
        return string.Empty;
    }
}
