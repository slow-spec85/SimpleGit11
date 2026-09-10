using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class SubmoduleViewItemTests
{
    [TestMethod]
    public void Constructor_UsesDefaultBranchAndNeutralStatusForPinnedCommit()
    {
        SubmoduleViewItem item = CreateItem(CreateSubmodule());

        Assert.AreEqual("SubmoduleDefaultBranch", item.BranchText);
        Assert.AreEqual("SubmoduleStatusReady", item.StatusText);
        Assert.IsFalse(item.IsStatusAccent);
        Assert.IsTrue(item.IsStatusNeutral);
        Assert.AreEqual("2222222", item.CommitText);
    }

    [TestMethod]
    public void Constructor_UsesAccentStatusForDifferentCommit()
    {
        GitSubmodule submodule = CreateSubmodule() with
        {
            Branch = "main",
            CheckedOutCommit = "3333333333333333333333333333333333333333",
        };

        SubmoduleViewItem item = CreateItem(submodule);

        Assert.AreEqual("main", item.BranchText);
        Assert.AreEqual("SubmoduleStatusDifferentCommit", item.StatusText);
        Assert.IsTrue(item.IsStatusAccent);
        Assert.IsFalse(item.IsStatusNeutral);
        Assert.AreEqual("2222222 → 3333333", item.CommitText);
    }

    [TestMethod]
    public void CopyTextCommand_CopiesProvidedValue()
    {
        string? copiedText = null;
        SubmoduleViewItem item = CreateItem(CreateSubmodule(), text => copiedText = text);

        item.CopyTextCommand.Execute(item.Url);

        Assert.AreEqual(item.Url, copiedText);
    }

    [TestMethod]
    public async Task OpenInNewWindowCommand_OpensSubmodulePath()
    {
        string? openedPath = null;
        SubmoduleViewItem item = CreateItem(
            CreateSubmodule(),
            openInNewWindow: path =>
            {
                openedPath = path;
                return Task.CompletedTask;
            });

        await item.OpenInNewWindowCommand.ExecuteAsync(null);

        Assert.AreEqual(item.FullPath, openedPath);
    }

    [TestMethod]
    public void OpenInNewWindowCommand_RemoteContext_IsDisabled()
    {
        SubmoduleViewItem item = CreateItem(CreateSubmodule(), canOpenLocalFolder: false);

        Assert.IsFalse(item.OpenInNewWindowCommand.CanExecute(null));
    }

    private static SubmoduleViewItem CreateItem(
        GitSubmodule submodule,
        Action<string>? copy = null,
        Func<string, Task>? openInNewWindow = null,
        bool canOpenLocalFolder = true) => new(
        submodule,
        new TestLocalizationService(),
        new ImmediateAsyncCommandExecutor(),
        _ => Task.CompletedTask,
        openInNewWindow ?? (_ => Task.CompletedTask),
        _ => { },
        (_, _) => Task.CompletedTask,
        copy ?? (_ => { }),
        "D:\\Repository",
        canOpenLocalFolder);

    private static GitSubmodule CreateSubmodule() => new(
        Name: "External/TextControlBox-WinUI",
        Path: "External/TextControlBox-WinUI",
        FullPath: "D:\\Repository\\External\\TextControlBox-WinUI",
        Url: "https://github.com/slow-spec85/TextControlBox-WinUI.git",
        Branch: "",
        HeadCommit: "2222222222222222222222222222222222222222",
        IndexCommit: "2222222222222222222222222222222222222222",
        CheckedOutCommit: "2222222222222222222222222222222222222222",
        IsInitialized: true,
        HasTrackedChanges: false,
        HasUntrackedFiles: false,
        HasConflict: false,
        ErrorMessage: "",
        Children: []);

    private sealed class ImmediateAsyncCommandExecutor : IAsyncCommandExecutor
    {
        public Task ExecuteAsync(Func<Task> operation) => operation();
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public AppLanguage CurrentLanguage => AppLanguage.English;

        public string GetString(string resourceKey) => resourceKey;

        public void ApplyLanguage()
        {
        }

        public void SetLanguage(AppLanguage language)
        {
        }
    }
}
