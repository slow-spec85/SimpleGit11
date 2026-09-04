using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class RemoteViewItemTests
{
    [TestMethod]
    public void CopyTextCommand_CopiesProvidedValue()
    {
        string? copiedText = null;
        RemoteViewItem item = new(
            new GitRemote("origin", "https://example.test/repository.git", ""),
            new TestLocalizationService(),
            new AsyncCommandExecutor(new RecordingExceptionHandler()),
            _ => { },
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            text => copiedText = text,
            false);

        item.CopyTextCommand.Execute(item.ReferenceText);

        Assert.AreEqual(item.ReferenceText, copiedText);
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
