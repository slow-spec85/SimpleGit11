using System.Diagnostics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SimpleGit11.Tests.Architecture;

[TestClass]
public sealed class TestHostConfigurationTests
{
    [TestMethod]
    [DataRow("SimpleGit11.Tests")]
    [DataRow("SimpleGit11.Plugin.Ssh.Tests")]
    public async Task ConsoleTestHost_DoesNotAdvertiseXamlDeployment(string projectName)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SimpleGit11.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory, "Could not locate the repository root.");
        ProcessStartInfo startInfo = new("dotnet")
        {
            WorkingDirectory = directory.FullName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[]
        {
            "msbuild", Path.Combine(projectName, projectName + ".csproj"),
            "-p:Platform=x64", "-p:DesignTimeBuild=true", "-p:BuildingInsideVisualStudio=true",
            "-getProperty:OutputType,WindowsPackageType,IsTestingPlatformApplication,AppxPackage,WindowsAppSDKSelfContained,WindowsAppSdkBootstrapInitialize",
            "-getItem:ProjectCapability"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        Assert.IsTrue(process.Start());
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        string output = await outputTask;
        string error = await errorTask;
        Assert.AreEqual(0, process.ExitCode, error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement properties = document.RootElement.GetProperty("Properties");
        Assert.AreEqual("Exe", properties.GetProperty("OutputType").GetString());
        Assert.AreEqual("None", properties.GetProperty("WindowsPackageType").GetString());
        Assert.AreEqual("true", properties.GetProperty("IsTestingPlatformApplication").GetString());
        Assert.AreEqual("false", properties.GetProperty("AppxPackage").GetString());
        Assert.AreEqual("true", properties.GetProperty("WindowsAppSDKSelfContained").GetString(),
            "Test hosts must carry their Windows App SDK runtime on clean CI machines.");
        Assert.AreNotEqual("true", properties.GetProperty("WindowsAppSdkBootstrapInitialize").GetString(),
            "The runtime bootstrapper can display an installation prompt before the test runner starts.");
        string[] capabilities = document.RootElement.GetProperty("Items").GetProperty("ProjectCapability")
            .EnumerateArray().Select(item => item.GetProperty("Identity").GetString()!).ToArray();
        CollectionAssert.Contains(capabilities, "TestContainer");
        CollectionAssert.Contains(capabilities, "TestingPlatformServer");
        Assert.IsFalse(capabilities.Any(capability => capability.StartsWith("WinUI", StringComparison.Ordinal)
            || capability.StartsWith("WindowsXaml", StringComparison.Ordinal)
            || capability == "WindowsUniversalMultiViews"),
            "Console tests must not select Visual Studio's XAML device deployment host.");
    }
}
