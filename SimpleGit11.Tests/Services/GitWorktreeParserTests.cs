using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitWorktreeParserTests
{
    [TestMethod]
    public void Parse_MultipleWorktrees_MapsFlagsAndReasons()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string mainPath = temporaryDirectory.CreateDirectory("main repo");
        string linkedPath = temporaryDirectory.CreateDirectory("ветка feature");
        RepositoryInfo repository = new(
            mainPath,
            "main",
            "main",
            mainWorktreePath: mainPath);
        string output =
            $"worktree {mainPath}\0" +
            "HEAD 1111111111111111111111111111111111111111\0" +
            "branch refs/heads/main\0" +
            $"worktree {linkedPath}\0" +
            "HEAD 2222222222222222222222222222222222222222\0" +
            "detached\0" +
            "locked maintenance\0" +
            "prunable missing administrative files\0";

        IReadOnlyList<GitWorktree> result = GitWorktreeParser.Parse(output, repository);

        Assert.HasCount(2, result);
        Assert.IsTrue(result[0].IsMain);
        Assert.IsTrue(result[0].IsCurrent);
        Assert.AreEqual("main", result[0].BranchName);
        Assert.IsTrue(result[1].IsDetached);
        Assert.IsTrue(result[1].IsLocked);
        Assert.AreEqual("maintenance", result[1].LockReason);
        Assert.IsTrue(result[1].IsPrunable);
        Assert.AreEqual("missing administrative files", result[1].PrunableReason);
        Assert.AreEqual(Path.GetFileName(linkedPath), result[1].DisplayName);
    }

    [TestMethod]
    public void Parse_EmptyOutput_ReturnsEmptyCollection()
    {
        using TemporaryDirectory temporaryDirectory = new();
        RepositoryInfo repository = new(
            temporaryDirectory.Path,
            "empty",
            "",
            mainWorktreePath: temporaryDirectory.Path);

        IReadOnlyList<GitWorktree> result = GitWorktreeParser.Parse("", repository);

        Assert.IsEmpty(result);
    }
}
