using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class FoundRepositoryViewItemTests
{
    private const string RepositoryPath = @"C:\Repositories\SampleRepository";
    private const string RepositoryName = "SampleRepository";

    [TestMethod]
    public async Task OpenCommand_OpensRepositoryPath()
    {
        string? openedPath = null;
        FoundRepositoryViewItem item = new(
            new RepositoryInfo(RepositoryPath, RepositoryName, "main"),
            new AsyncCommandExecutor(new RecordingExceptionHandler()),
            path =>
            {
                openedPath = path;
                return Task.CompletedTask;
            },
            _ => { },
            _ => { });

        await item.OpenCommand.ExecuteAsync(null);

        Assert.AreEqual(RepositoryPath, openedPath);
    }

    [TestMethod]
    public void OpenFolderCommand_OpensRepositoryFolder()
    {
        string? openedPath = null;
        FoundRepositoryViewItem item = new(
            new RepositoryInfo(RepositoryPath, RepositoryName, "main"),
            new AsyncCommandExecutor(new RecordingExceptionHandler()),
            _ => Task.CompletedTask,
            path => openedPath = path,
            _ => { });

        item.OpenFolderCommand.Execute(null);

        Assert.AreEqual(RepositoryPath, openedPath);
    }

    [TestMethod]
    public void OpenFolderCommand_RemoteContext_IsDisabled()
    {
        FoundRepositoryViewItem item = new(
            new RepositoryInfo(RepositoryPath, RepositoryName, "main"),
            new AsyncCommandExecutor(new RecordingExceptionHandler()),
            _ => Task.CompletedTask,
            _ => Assert.Fail("The local folder callback must not run in an SSH context."),
            _ => { },
            canOpenLocalFolder: false);

        Assert.IsFalse(item.OpenFolderCommand.CanExecute(null));
    }

    [TestMethod]
    public void CopyCommands_CopyCorrespondingValues()
    {
        string? copiedText = null;
        FoundRepositoryViewItem item = new(
            new RepositoryInfo(RepositoryPath, RepositoryName, "main"),
            new AsyncCommandExecutor(new RecordingExceptionHandler()),
            _ => Task.CompletedTask,
            _ => { },
            text => copiedText = text);

        item.CopyPathCommand.Execute(null);
        Assert.AreEqual(RepositoryPath, copiedText);

        item.CopyNameCommand.Execute(null);
        Assert.AreEqual(RepositoryName, copiedText);
    }

    private sealed class RecordingExceptionHandler : IAsyncCommandExceptionHandler
    {
        public void Handle(System.Exception exception)
        {
        }
    }
}
