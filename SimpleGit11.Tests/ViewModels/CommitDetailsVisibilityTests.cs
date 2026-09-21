using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class CommitDetailsVisibilityTests
{
    [TestMethod]
    public void Toggle_DoesNotReplaceCommitRowsOrSelection()
    {
        Browser browser = new();
        GitCommit commit = new("head", "head", "Author", "author@example.invalid", null, "Head", "Head");
        browser.Load([commit]);

        Assert.IsFalse(browser.IsCommitDetailsBlockVisible);
        browser.IsCommitDetailsBlockVisible = true;
        Assert.AreEqual(Visibility.Visible, browser.CommitDetailsBlockVisibility);
        Assert.AreSame(commit, browser.SelectedCommit);
        Assert.HasCount(1, browser.CommitRows);

        browser.IsCommitDetailsBlockVisible = false;
        Assert.AreEqual(Visibility.Collapsed, browser.CommitDetailsBlockVisibility);
        Assert.AreSame(commit, browser.SelectedCommit);
    }

    private sealed class Browser : CommitBrowserViewModelBase
    {
        public Browser()
            : base(
                null!,
                CommitGraphVisibilityTests.Stub.Create<IGitService>((method, _) => method == "ExecuteAsync"
                    ? Task.CompletedTask
                    : throw new NotSupportedException(method)),
                CommitGraphVisibilityTests.Stub.Create<ILocalizationService>((method, args) => method == "GetString"
                    ? args![0]
                    : throw new NotSupportedException(method)),
                null!, null!, null!, new StrongReferenceMessenger(), "none", "file", "repo")
        {
        }

        protected override bool IsCommitDetailsOperationRunning => false;
        protected override void ShowCommitDetailsError(string message, string? details = null) { }
        public void Load(IReadOnlyList<GitCommit> commits) => ReplaceCommits(commits);
    }
}
