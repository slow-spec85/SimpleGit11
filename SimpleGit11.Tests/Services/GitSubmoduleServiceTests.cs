using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitSubmoduleServiceTests
{
    private const string HeadCommit = "1111111111111111111111111111111111111111";
    private const string IndexCommit = "2222222222222222222222222222222222222222";
    private const string CheckedOutCommit = "3333333333333333333333333333333333333333";

    [TestMethod]
    public async Task GetSubmodulesAsync_ReadsConfigurationAndWorkingTreeState()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string repositoryPath = temporaryDirectory.CreateDirectory("repository");
        string submodulePath = temporaryDirectory.CreateDirectory("repository/External/TextControlBox-WinUI");
        temporaryDirectory.CreateFile("repository/.gitmodules");
        temporaryDirectory.CreateFile("repository/External/TextControlBox-WinUI/.git");

        FakeGitCommandRunner runner = new(repositoryPath, submodulePath);
        GitSubmoduleService service = new(runner);
        RepositoryInfo repository = new(repositoryPath, "repository", "main");

        IReadOnlyList<GitSubmodule> result = await service.GetSubmodulesAsync(repository);

        Assert.HasCount(1, result);
        GitSubmodule submodule = result[0];
        Assert.AreEqual("TextControlBox", submodule.Name);
        Assert.AreEqual("External/TextControlBox-WinUI", submodule.Path);
        Assert.AreEqual("https://example.test/TextControlBox-WinUI.git", submodule.Url);
        Assert.AreEqual("master", submodule.Branch);
        Assert.AreEqual(HeadCommit, submodule.HeadCommit);
        Assert.AreEqual(IndexCommit, submodule.IndexCommit);
        Assert.AreEqual(CheckedOutCommit, submodule.CheckedOutCommit);
        Assert.IsTrue(submodule.IsInitialized);
        Assert.IsTrue(submodule.IsCommitChanged);
        Assert.IsTrue(submodule.IsStaged);
        Assert.IsTrue(submodule.HasTrackedChanges);
        Assert.IsTrue(submodule.HasUntrackedFiles);
        Assert.IsTrue(submodule.IsDirty);
        Assert.IsFalse(submodule.HasConflict);
        Assert.IsFalse(submodule.HasError);
    }

    [TestMethod]
    public async Task GetSubmodulesAsync_BuildsRecursiveTreeForInitializedSubmodules()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string repositoryPath = temporaryDirectory.CreateDirectory("repository");
        string submodulePath = temporaryDirectory.CreateDirectory("repository/External/TextControlBox-WinUI");
        string nestedSubmodulePath = temporaryDirectory.CreateDirectory(
            "repository/External/TextControlBox-WinUI/External/SyntaxDefinitions");
        temporaryDirectory.CreateFile("repository/.gitmodules");
        temporaryDirectory.CreateFile("repository/External/TextControlBox-WinUI/.git");
        temporaryDirectory.CreateFile("repository/External/TextControlBox-WinUI/.gitmodules");

        FakeGitCommandRunner runner = new(repositoryPath, submodulePath, nestedSubmodulePath);
        GitSubmoduleService service = new(runner);

        IReadOnlyList<GitSubmodule> result = await service.GetSubmodulesAsync(
            new RepositoryInfo(repositoryPath, "repository", "main"));

        Assert.HasCount(1, result);
        Assert.HasCount(1, result[0].Children);
        GitSubmodule nestedSubmodule = result[0].Children[0];
        Assert.AreEqual("SyntaxDefinitions", nestedSubmodule.Name);
        Assert.AreEqual("External/SyntaxDefinitions", nestedSubmodule.Path);
        Assert.AreEqual(Path.GetFullPath(nestedSubmodulePath), nestedSubmodule.FullPath);
        Assert.IsFalse(nestedSubmodule.IsInitialized);
    }

    [TestMethod]
    public async Task GetApplicationStatesAsync_ReturnsNestedPathsThatDoNotMatchIndex()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string repositoryPath = temporaryDirectory.CreateDirectory("repository");
        string submodulePath = temporaryDirectory.CreateDirectory("repository/External/TextControlBox-WinUI");
        string nestedSubmodulePath = temporaryDirectory.CreateDirectory(
            "repository/External/TextControlBox-WinUI/External/SyntaxDefinitions");
        temporaryDirectory.CreateFile("repository/.gitmodules");
        temporaryDirectory.CreateFile("repository/External/TextControlBox-WinUI/.git");
        temporaryDirectory.CreateFile("repository/External/TextControlBox-WinUI/.gitmodules");

        GitSubmoduleService service = new(new FakeGitCommandRunner(
            repositoryPath,
            submodulePath,
            nestedSubmodulePath));

        IReadOnlyList<GitSubmoduleApplicationState> states = await service.GetApplicationStatesAsync(
            new RepositoryInfo(repositoryPath, "repository", "main"));

        Assert.HasCount(2, states);
        Assert.AreEqual("External/TextControlBox-WinUI", states[0].Path);
        Assert.AreEqual(repositoryPath, states[0].OwnerRepositoryPath);
        Assert.AreEqual(
            "External/TextControlBox-WinUI/External/SyntaxDefinitions",
            states[1].Path);
        Assert.AreEqual(submodulePath.Replace('/', '\\'), states[1].OwnerRepositoryPath);
        Assert.IsFalse(states[1].IsInitialized);
    }

    [TestMethod]
    public void ConfigurationParser_PreservesNamesContainingDots()
    {
        string output = string.Join('\0',
            "submodule.vendor.editor.path\nExternal/Editor",
            "submodule.vendor.editor.url\n../Editor.git",
            "submodule.vendor.editor.branch\nrelease",
            "");

        IReadOnlyList<GitSubmoduleConfiguration> result =
            GitSubmoduleConfigurationParser.Parse(output);

        Assert.HasCount(1, result);
        Assert.AreEqual("vendor.editor", result[0].Name);
        Assert.AreEqual("External/Editor", result[0].Path);
        Assert.AreEqual("../Editor.git", result[0].Url);
        Assert.AreEqual("release", result[0].Branch);
    }

    [TestMethod]
    public async Task ManagementOperations_BuildSafePathScopedCommands()
    {
        RecordingGitCommandRunner runner = new();
        GitSubmoduleService service = new(runner);
        RepositoryInfo repository = new("C:\\repository", "repository", "main");

        await service.AddAsync(repository, new SubmoduleAddRequest(
            "https://example.test/library.git",
            "External/Library",
            "develop"));
        await service.InitializeAsync(repository.Path, "External/Library");
        await service.CheckoutRecordedAsync(repository.Path, "External/Library");
        await service.UpdateFromRemoteAsync(repository.Path, "External/Library");
        await service.SyncAsync(repository.Path, "External/Library");
        await service.ApplyPinnedAsync(repository.Path, "External/Library");
        await service.SetUrlAsync(
            repository.Path,
            "External/Library",
            "https://example.test/new-library.git");
        await service.SetBranchAsync(repository.Path, "External/Library", "release");
        await service.SetBranchAsync(repository.Path, "External/Library", "");
        await service.DeinitializeAsync(repository.Path, "External/Library");
        await service.RemoveAsync(repository.Path, "External/Library");

        CollectionAssert.AreEqual(
            new[]
            {
                "submodule add --branch develop -- https://example.test/library.git External/Library",
                "submodule update --init --recursive -- External/Library",
                "submodule update --checkout --recursive -- External/Library",
                "submodule update --remote --checkout --recursive -- External/Library",
                "submodule sync --recursive -- External/Library",
                "submodule sync --recursive -- External/Library",
                "submodule update --init --checkout --recursive -- External/Library",
                "submodule set-url -- External/Library https://example.test/new-library.git",
                "submodule set-branch --branch release -- External/Library",
                "submodule set-branch --default -- External/Library",
                "submodule deinit -- External/Library",
                "submodule deinit -- External/Library",
                "rm -- External/Library"
            },
            runner.Commands);
    }

    [TestMethod]
    public async Task GetReferenceChangesAsync_ReturnsOnlyChangedGitlinks()
    {
        ReferenceComparisonGitCommandRunner runner = new();
        GitSubmoduleService service = new(runner);

        IReadOnlyList<GitSubmoduleReferenceChange> changes = await service.GetReferenceChangesAsync(
            "C:\\repository",
            "origin/main",
            "main");

        Assert.HasCount(3, changes);
        Assert.AreEqual("External/Added", changes[0].Path);
        Assert.AreEqual(GitSubmoduleReferenceChangeKind.Added, changes[0].Kind);
        Assert.AreEqual("External/Removed", changes[1].Path);
        Assert.AreEqual(GitSubmoduleReferenceChangeKind.Removed, changes[1].Kind);
        Assert.AreEqual("External/Updated", changes[2].Path);
        Assert.AreEqual(GitSubmoduleReferenceChangeKind.Updated, changes[2].Kind);
        Assert.AreEqual("3333333333333333333333333333333333333333", changes[2].OldCommit);
        Assert.AreEqual("4444444444444444444444444444444444444444", changes[2].NewCommit);
    }

    private sealed class FakeGitCommandRunner(
        string repositoryPath,
        string submodulePath,
        string? nestedSubmodulePath = null) : IGitCommandRunner
    {
        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string command = string.Join(' ', arguments);
            string output = "";

            if (PathsEqual(workingDirectory, repositoryPath)
                && command == "config --null --file .gitmodules --list")
            {
                output = string.Join('\0',
                    "submodule.TextControlBox.path\nExternal/TextControlBox-WinUI",
                    "submodule.TextControlBox.url\nhttps://example.test/TextControlBox-WinUI.git",
                    "submodule.TextControlBox.branch\nmaster",
                    "");
            }
            else if (PathsEqual(workingDirectory, submodulePath)
                && command == "config --null --file .gitmodules --list")
            {
                output = string.Join('\0',
                    "submodule.SyntaxDefinitions.path\nExternal/SyntaxDefinitions",
                    "submodule.SyntaxDefinitions.url\n../SyntaxDefinitions.git",
                    "");
            }
            else if (PathsEqual(workingDirectory, repositoryPath)
                && command.StartsWith("ls-tree ", StringComparison.Ordinal))
            {
                output = $"160000 commit {HeadCommit}\tExternal/TextControlBox-WinUI\0";
            }
            else if (PathsEqual(workingDirectory, repositoryPath)
                && command.StartsWith("ls-files ", StringComparison.Ordinal))
            {
                output = $"160000 {IndexCommit} 0\tExternal/TextControlBox-WinUI\0";
            }
            else if (PathsEqual(workingDirectory, submodulePath)
                && command.StartsWith("ls-tree ", StringComparison.Ordinal))
            {
                output = $"160000 commit {HeadCommit}\tExternal/SyntaxDefinitions\0";
            }
            else if (PathsEqual(workingDirectory, submodulePath)
                && command.StartsWith("ls-files ", StringComparison.Ordinal))
            {
                output = $"160000 {IndexCommit} 0\tExternal/SyntaxDefinitions\0";
            }
            else if (PathsEqual(workingDirectory, submodulePath)
                && command.StartsWith("rev-parse ", StringComparison.Ordinal))
            {
                output = CheckedOutCommit;
            }
            else if (PathsEqual(workingDirectory, submodulePath)
                && command.StartsWith("status ", StringComparison.Ordinal))
            {
                output = "1 .M N... 100644 100644 100644 abc def file.cs\0? notes.txt\0";
            }

            if (nestedSubmodulePath is not null
                && command.StartsWith("rev-parse ", StringComparison.Ordinal)
                && PathsEqual(workingDirectory, nestedSubmodulePath))
            {
                Assert.Fail("An uninitialized nested submodule must not be queried as a repository.");
            }

            return Task.FromResult(new GitCommandResult(0, output, ""));
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class RecordingGitCommandRunner : IGitCommandRunner
    {
        public List<string> Commands { get; } = [];

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(string.Join(' ', arguments));
            return Task.FromResult(new GitCommandResult(0, "", ""));
        }
    }

    private sealed class ReferenceComparisonGitCommandRunner : IGitCommandRunner
    {
        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string revision = arguments[^1];
            string output = revision == "origin/main"
                ? string.Join('\0',
                    "100644 blob aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\tREADME.md",
                    "160000 commit 2222222222222222222222222222222222222222\tExternal/Removed",
                    "160000 commit 3333333333333333333333333333333333333333\tExternal/Updated",
                    "")
                : string.Join('\0',
                    "100644 blob bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\tREADME.md",
                    "160000 commit 1111111111111111111111111111111111111111\tExternal/Added",
                    "160000 commit 4444444444444444444444444444444444444444\tExternal/Updated",
                    "");
            return Task.FromResult(new GitCommandResult(0, output, ""));
        }
    }
}
