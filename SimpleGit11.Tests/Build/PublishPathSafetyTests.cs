using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SimpleGit11.Tests.Build;

[TestClass]
public sealed class PublishPathSafetyTests
{
    [TestMethod]
    public void CiWorkflow_RunsBuiltApplicationAndSshTestsWithoutEvaluatingSolution()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workflowPath = Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml");
        string[] testCommands = File.ReadLines(workflowPath)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("run: dotnet test ", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(1, testCommands.Length, "CI must run the built test modules in one invocation.");
        const string modulePattern = "SimpleGit11*.Tests/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/SimpleGit11*.Tests.dll";
        StringAssert.Contains(testCommands[0], $"--test-modules \"{modulePattern}\"");
        Assert.IsFalse(testCommands[0].Contains("--solution", StringComparison.Ordinal));
        Assert.IsFalse(testCommands[0].Contains("--project", StringComparison.Ordinal));
        StringAssert.Contains(testCommands[0], "--max-parallel-test-modules 1");
        StringAssert.Contains(testCommands[0], "--output Detailed");
        StringAssert.Contains(testCommands[0], "--timeout 5m");
        StringAssert.Contains(testCommands[0], "--diagnostic --diagnostic-output-directory TestResults/diagnostics");
        StringAssert.Contains(testCommands[0], "--report-trx --results-directory TestResults");

        XDocument solution = XDocument.Load(Path.Combine(repositoryRoot, "SimpleGit11.slnx"));
        string[] projectPaths = solution.Descendants("Project")
            .Select(project => (string?)project.Attribute("Path") ?? string.Empty)
            .ToArray();
        CollectionAssert.Contains(projectPaths, "SimpleGit11.Tests/SimpleGit11.Tests.csproj");
        CollectionAssert.Contains(projectPaths, "SimpleGit11.Plugin.Ssh.Tests/SimpleGit11.Plugin.Ssh.Tests.csproj");
        foreach (string projectName in new[] { "SimpleGit11.Tests", "SimpleGit11.Plugin.Ssh.Tests" })
        {
            string modulePath = $"{projectName}/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/{projectName}.dll";
            Assert.IsTrue(System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(modulePattern, modulePath),
                $"CI must include the test module for {projectName}.");
        }
    }

    [TestMethod]
    public void CiWorkflow_PreservesDiagnosticsWhenTestsFailOrTimeOut()
    {
        string workflow = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "ci.yml"));
        int testStep = workflow.IndexOf("- name: Test application and SSH plugin", StringComparison.Ordinal);
        int diagnosticStep = workflow.IndexOf("- name: Upload test diagnostics", StringComparison.Ordinal);
        Assert.IsTrue(testStep >= 0 && diagnosticStep > testStep);
        string testConfiguration = workflow[testStep..diagnosticStep];
        StringAssert.Contains(testConfiguration, "timeout-minutes: 10");
        string diagnosticConfiguration = workflow[diagnosticStep..];
        StringAssert.Contains(diagnosticConfiguration, "if: ${{ always() }}");
        StringAssert.Contains(diagnosticConfiguration, "uses: actions/upload-artifact@");
        StringAssert.Contains(diagnosticConfiguration, "path: TestResults/");
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
