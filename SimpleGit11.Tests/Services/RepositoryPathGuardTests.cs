using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Services;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class RepositoryPathGuardTests
{
    [TestMethod]
    public void GetSafeFilePath_FileInsideRepository_ReturnsFullPath()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string repositoryPath = temporaryDirectory.CreateDirectory("repo");
        string expectedPath = temporaryDirectory.CreateFile(
            Path.Combine("repo", "src", "данные file.txt"));

        string result = RepositoryPathGuard.GetSafeFilePath(
            repositoryPath,
            Path.Combine("src", "данные file.txt"));

        Assert.AreEqual(Path.GetFullPath(expectedPath), result, ignoreCase: true);
    }

    [TestMethod]
    public void GetSafeFilePath_SiblingWithCommonPrefix_Throws()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string repositoryPath = temporaryDirectory.CreateDirectory("repo");
        string siblingFile = temporaryDirectory.CreateFile(
            Path.Combine("repo-evil", "secret.txt"),
            "secret");

        Assert.ThrowsExactly<FileNotFoundException>(
            () => RepositoryPathGuard.GetSafeFilePath(repositoryPath, siblingFile));
    }

    [TestMethod]
    public void GetSafeFilePath_ParentTraversal_Throws()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string repositoryPath = temporaryDirectory.CreateDirectory("repo");
        temporaryDirectory.CreateFile("outside.txt", "outside");

        Assert.ThrowsExactly<FileNotFoundException>(
            () => RepositoryPathGuard.GetSafeFilePath(
                repositoryPath,
                Path.Combine("..", "outside.txt")));
    }

    [TestMethod]
    public void IsPathInsideRepository_PathComparisonIsCaseInsensitive()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string repositoryPath = temporaryDirectory.CreateDirectory("Repo");
        string filePath = temporaryDirectory.CreateFile(
            Path.Combine("Repo", "Nested", "file.txt"));

        bool result = RepositoryPathGuard.IsPathInsideRepository(
            repositoryPath.ToUpperInvariant(),
            filePath.ToLowerInvariant());

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void GetSafeFilePath_SymbolicLinkOutsideRepository_Throws()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string repositoryPath = temporaryDirectory.CreateDirectory("repo");
        string outsidePath = temporaryDirectory.CreateDirectory("outside");
        temporaryDirectory.CreateFile(Path.Combine("outside", "secret.txt"), "secret");
        string linkPath = Path.Combine(repositoryPath, "linked");
        Directory.CreateSymbolicLink(linkPath, outsidePath);

        Assert.ThrowsExactly<FileNotFoundException>(
            () => RepositoryPathGuard.GetSafeFilePath(
                repositoryPath,
                Path.Combine("linked", "secret.txt")));
    }
}
