using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Presentation.Services;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class ApplicationInstanceLauncherTests
{
    [TestMethod]
    public void GetRepositoryPath_OpenRepositoryOption_ReturnsFollowingPath()
    {
        string repositoryPath = @"D:\Repositories\Repository with spaces";

        string? result = ApplicationLaunchArguments.GetRepositoryPath(
            [@"D:\Applications\SimpleGit11.exe", "--open-repository", repositoryPath]);

        Assert.AreEqual(repositoryPath, result);
    }

    [TestMethod]
    public void GetRepositoryPath_MissingValue_ReturnsNull()
    {
        string? result = ApplicationLaunchArguments.GetRepositoryPath(
            [@"D:\Applications\SimpleGit11.exe", "--open-repository"]);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void CreateStartInfo_PreservesRepositoryPathAsSingleArgument()
    {
        string executablePath = @"D:\Applications\SimpleGit11.exe";
        string repositoryPath = @"D:\Repositories\Repository with spaces";

        ProcessStartInfo startInfo = ApplicationInstanceLauncher.CreateStartInfo(
            executablePath,
            repositoryPath);

        Assert.AreEqual(executablePath, startInfo.FileName);
        Assert.IsFalse(startInfo.UseShellExecute);
        CollectionAssert.AreEqual(
            new[] { "--open-repository", repositoryPath },
            startInfo.ArgumentList.ToArray());
    }
}
