using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SimpleGit11.Tests.Build;

[TestClass]
public sealed class PublishPathSafetyTests
{
    [TestMethod]
    public void CiWorkflow_RunsAllSolutionTestsIncludingSshPlugin()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workflowPath = Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml");
        string[] testCommands = File.ReadLines(workflowPath)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("run: dotnet test ", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(1, testCommands.Length, "CI must have one solution-wide test invocation.");
        StringAssert.Contains(testCommands[0], "--solution SimpleGit11.slnx");
        StringAssert.Contains(testCommands[0], "--configuration Release");
        StringAssert.Contains(testCommands[0], "-p:Platform=x64");

        XDocument solution = XDocument.Load(Path.Combine(repositoryRoot, "SimpleGit11.slnx"));
        string[] projectPaths = solution.Descendants("Project")
            .Select(project => (string?)project.Attribute("Path") ?? string.Empty)
            .ToArray();
        CollectionAssert.Contains(projectPaths, "SimpleGit11.Tests/SimpleGit11.Tests.csproj");
        CollectionAssert.Contains(projectPaths, "SimpleGit11.Plugin.Ssh.Tests/SimpleGit11.Plugin.Ssh.Tests.csproj");
    }

    [TestMethod]
    [DataRow("PublishPathSafety.Tests.ps1", "-PathSafetyScript", "Publish-PathSafety.ps1")]
    [DataRow("InstallerPayload.Tests.ps1", "-PayloadScript", "Installer-Payload.ps1")]
    [DataRow("PublishRelease.Tests.ps1", "-PublishScript", "Publish-Release.ps1")]
    [DataRow("ReleaseCiReuse.Tests.ps1", "-CiReuseScript", "Get-ReleaseCiReuse.ps1")]
    public async Task PublicationScripts_ValidateSafetyAndPayload(string testFile, string parameter, string buildFile)
    {
        string repositoryRoot = FindRepositoryRoot();
        string testScript = Path.Combine(
            repositoryRoot,
            "SimpleGit11.Tests",
            "Build",
            testFile);
        string pathSafetyScript = Path.Combine(
            repositoryRoot,
            "SimpleGit11",
            "Build",
            buildFile);

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
        startInfo.ArgumentList.Add(parameter);
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
