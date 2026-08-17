using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitChangeRecoveryArgumentsTests
{
    [TestMethod]
    public void CreateDiscardFileCommands_UntrackedPath_PlacesSeparatorBeforePath()
    {
        GitChangedFile changedFile = new("-dangerous name.txt", "Untracked");

        IReadOnlyList<IReadOnlyList<string>> commands =
            GitChangeRecoveryArguments.CreateDiscardFileCommands(changedFile);

        Assert.HasCount(1, commands);
        CollectionAssert.AreEqual(
            new[] { "clean", "-f", "--", "-dangerous name.txt" },
            commands[0].ToArray());
    }

    [TestMethod]
    public void CreateDiscardFileCommands_AddedFile_UnstagesThenCleansOnlyThatPath()
    {
        GitChangedFile changedFile = new("folder/new file.txt", "Added");

        IReadOnlyList<IReadOnlyList<string>> commands =
            GitChangeRecoveryArguments.CreateDiscardFileCommands(changedFile);

        Assert.HasCount(2, commands);
        CollectionAssert.AreEqual(
            new[] { "restore", "--staged", "--", "folder/new file.txt" },
            commands[0].ToArray());
        CollectionAssert.AreEqual(
            new[] { "clean", "-f", "--", "folder/new file.txt" },
            commands[1].ToArray());
    }

    [TestMethod]
    public void CreateDiscardFileCommands_ModifiedFile_RestoresIndexAndWorktree()
    {
        GitChangedFile changedFile = new("tracked.txt", "Modified");

        IReadOnlyList<IReadOnlyList<string>> commands =
            GitChangeRecoveryArguments.CreateDiscardFileCommands(changedFile);

        Assert.HasCount(1, commands);
        CollectionAssert.AreEqual(
            new[] { "restore", "--staged", "--worktree", "--", "tracked.txt" },
            commands[0].ToArray());
    }

    [TestMethod]
    [DataRow("soft", "--soft")]
    [DataRow("mixed", "--mixed")]
    [DataRow("hard", "--hard")]
    public void CreateResetArguments_SupportedMode_MapsToExpectedSwitch(
        string mode,
        string expectedSwitch)
    {
        IReadOnlyList<string> arguments =
            GitChangeRecoveryArguments.CreateResetArguments("abc123", mode);

        CollectionAssert.AreEqual(
            new[] { "reset", expectedSwitch, "abc123" },
            arguments.ToArray());
    }

    [TestMethod]
    public void CreateResetArguments_UnsupportedMode_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => GitChangeRecoveryArguments.CreateResetArguments("abc123", "merge"));
    }
}
