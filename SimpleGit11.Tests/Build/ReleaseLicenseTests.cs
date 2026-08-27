using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Build;

[TestClass]
public sealed class ReleaseLicenseTests
{
    [TestMethod]
    public async Task LicenseCollector_CreatesCompleteReleaseBundle()
    {
        string repositoryRoot = FindRepositoryRoot();
        string projectAssetsPath = Path.Combine(
            repositoryRoot,
            "SimpleGit11",
            "obj",
            "project.assets.json");
        string collectorPath = Path.Combine(
            repositoryRoot,
            "SimpleGit11",
            "Build",
            "Collect-ReleaseLicenses.ps1");
        string sourceComponentsPath = Path.Combine(
            repositoryRoot,
            "SimpleGit11",
            "Build",
            "SourceComponents.json");

        using TemporaryDirectory temporaryDirectory = new();
        string publishedDirectory = temporaryDirectory.CreateDirectory("publish");
        temporaryDirectory.CreateFile(Path.Combine("publish", "Collections.Pooled.dll"));
        temporaryDirectory.CreateFile(Path.Combine("publish", "CommunityToolkit.Mvvm.dll"));
        temporaryDirectory.CreateFile(Path.Combine("publish", "Microsoft.Graphics.Canvas.dll"));
        temporaryDirectory.CreateFile(Path.Combine("publish", "Microsoft.UI.dll"));
        temporaryDirectory.CreateFile(Path.Combine("publish", "Newtonsoft.Json.dll"));
        temporaryDirectory.CreateFile(Path.Combine("publish", "TextControlBox.dll"));
        temporaryDirectory.CreateFile(Path.Combine("publish", "WebView2Loader.dll"));
        temporaryDirectory.CreateFile(Path.Combine("publish", "coreclr.dll"));

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
        startInfo.ArgumentList.Add(collectorPath);
        startInfo.ArgumentList.Add("-RepositoryRoot");
        startInfo.ArgumentList.Add(repositoryRoot);
        startInfo.ArgumentList.Add("-ProjectAssetsPath");
        startInfo.ArgumentList.Add(projectAssetsPath);
        startInfo.ArgumentList.Add("-SourceComponentsPath");
        startInfo.ArgumentList.Add(sourceComponentsPath);
        startInfo.ArgumentList.Add("-PublishedDirectory");
        startInfo.ArgumentList.Add(publishedDirectory);

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
            $"Release license collection failed.{Environment.NewLine}" +
            $"STDOUT:{Environment.NewLine}{standardOutput}{Environment.NewLine}" +
            $"STDERR:{Environment.NewLine}{standardError}");

        Assert.IsTrue(File.Exists(Path.Combine(publishedDirectory, "LICENSE")));
        Assert.IsTrue(File.Exists(Path.Combine(publishedDirectory, "THIRD-PARTY-NOTICES.txt")));

        string packageIndexPath = Path.Combine(publishedDirectory, "Licenses", "PACKAGES.txt");
        Assert.IsTrue(File.Exists(packageIndexPath));
        string packageIndex = await File.ReadAllTextAsync(packageIndexPath);

        StringAssert.Contains(packageIndex, "Package: Collections.Pooled");
        StringAssert.Contains(packageIndex, "Package: CommunityToolkit.Mvvm");
        StringAssert.Contains(packageIndex, "Package: Microsoft.Graphics.Win2D");
        StringAssert.Contains(packageIndex, "Package: Microsoft.NETCore.App.Runtime.win-x64");
        StringAssert.Contains(packageIndex, "Package: Microsoft.WindowsAppSDK");
        StringAssert.Contains(packageIndex, "Package: Microsoft.Web.WebView2");
        StringAssert.Contains(packageIndex, "Component: TextControlBox.WinUI.slow-spec85");
        Assert.IsFalse(packageIndex.Contains(
            "Package: TextControlBox.WinUI.slow-spec85",
            StringComparison.Ordinal));
        Assert.IsFalse(packageIndex.Contains("Package: MinVer", StringComparison.Ordinal));

        string[] packageIndexLines = packageIndex.Split(
            Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries);
        int componentLineIndex = Array.FindIndex(
            packageIndexLines,
            line => line.Equals(
                "Component: TextControlBox.WinUI.slow-spec85",
                StringComparison.Ordinal));
        Assert.IsTrue(componentLineIndex >= 0);
        string revisionLine = packageIndexLines[componentLineIndex + 1];
        StringAssert.StartsWith(revisionLine, "Revision: ");
        string revision = revisionLine["Revision: ".Length..];
        Assert.AreEqual(40, revision.Length);
        Assert.IsTrue(revision.All(Uri.IsHexDigit));

        string[] sourceComponentLicenses = Directory.GetFiles(
            Path.Combine(publishedDirectory, "Licenses"),
                "LICENSE.txt",
                SearchOption.AllDirectories)
            .Where(path => (Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty)
                .StartsWith(
                    "TextControlBox.WinUI.slow-spec85-",
                    StringComparison.Ordinal))
            .ToArray();
        Assert.AreEqual(1, sourceComponentLicenses.Length);
        string sourceComponentLicense = await File.ReadAllTextAsync(sourceComponentLicenses[0]);
        StringAssert.Contains(
            sourceComponentLicense,
            "Copyright (c) 2024-2026 Julius Kirsch");

        string[] vendorNoticeFiles = Directory.GetFiles(
            Path.Combine(publishedDirectory, "Licenses"),
            "*",
            SearchOption.AllDirectories);
        Assert.IsTrue(vendorNoticeFiles.Length > 3);
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
