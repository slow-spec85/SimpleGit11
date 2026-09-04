using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class WorktreeViewItemTests
{
    [TestMethod]
    public void CopyTextCommand_CopiesProvidedValue()
    {
        string? copiedText = null;
        WorktreeViewItem item = new(
            new GitWorktree(
                "D:\\Repositories\\Sample-worktree",
                "1234567890",
                "feature/sample",
                false,
                false,
                false,
                false),
            new TestLocalizationService(),
            new AsyncCommandExecutor(new RecordingExceptionHandler()),
            _ => { },
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            text => copiedText = text);

        item.CopyTextCommand.Execute(item.Path);

        Assert.AreEqual(item.Path, copiedText);
    }

    [TestMethod]
    public void OpenFolderCommand_RemoteContext_IsDisabled()
    {
        WorktreeViewItem item = new(
            new GitWorktree("/srv/repo", "1234567890", "main", false, false, false, false),
            new TestLocalizationService(),
            new AsyncCommandExecutor(new RecordingExceptionHandler()),
            _ => Assert.Fail("The local folder callback must not run in an SSH context."),
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => { },
            canOpenLocalFolder: false);

        Assert.IsFalse(item.OpenFolderCommand.CanExecute(null));
    }

    private sealed class RecordingExceptionHandler : IAsyncCommandExceptionHandler
    {
        public void Handle(Exception exception)
        {
        }
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
