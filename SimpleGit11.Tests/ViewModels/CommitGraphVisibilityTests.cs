using System.Reflection;
using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class CommitGraphVisibilityTests
{
    [TestMethod]
    public void GraphToggle_RebuildsRowsWithoutChangingFilteredCommitsOrSelection()
    {
        Browser browser = new(showGraph: true);
        GitCommit child = CreateCommit("child", "parent");
        GitCommit parent = CreateCommit("parent");
        browser.Load([child, parent]);

        Assert.IsTrue(browser.IsCommitGraphVisible);
        Assert.IsTrue(browser.CommitRows.All(row => row.ShowGraph));
        Assert.AreSame(child, browser.SelectedCommit);
        CommitBrowserRowViewItem selectedRow = browser.SelectedCommitRow!;
        browser.SetSelectedCommits([child]);
        List<string?> changedProperties = [];
        selectedRow.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        browser.IsCommitGraphVisible = false;
        Assert.IsTrue(browser.CommitRows.All(row => !row.ShowGraph && row.AccessibleGraphDescription == ""));
        Assert.AreSame(child, browser.SelectedCommit);
        Assert.AreEqual("child", browser.SelectedCommitRow?.Commit.Hash);
        Assert.AreSame(selectedRow, browser.SelectedCommitRow);
        Assert.AreSame(selectedRow, browser.CommitRows[0]);
        Assert.AreSame(child, browser.SelectedCommits.Single());
        CollectionAssert.Contains(changedProperties, nameof(CommitBrowserRowViewItem.Graph));
        CollectionAssert.Contains(changedProperties, nameof(CommitBrowserRowViewItem.ShowGraph));
        Assert.HasCount(2, browser.Commits);

        browser.IsCommitGraphVisible = true;
        Assert.IsTrue(browser.CommitRows.All(row => row.ShowGraph));
        Assert.AreSame(child, browser.SelectedCommit);
        Assert.AreEqual("child", browser.SelectedCommitRow?.Commit.Hash);
        Assert.AreSame(selectedRow, browser.SelectedCommitRow);
        Assert.AreSame(selectedRow, browser.CommitRows[0]);
        Assert.AreSame(child, browser.SelectedCommits.Single());
    }

    [TestMethod]
    public void BrowserWithoutGraph_DoesNotOfferToggleOrDrawGraph()
    {
        Browser browser = new(showGraph: false);
        browser.Load([CreateCommit("child")]);

        Assert.IsFalse(browser.CommitRows.Single().ShowGraph);
    }

    [TestMethod]
    public void GraphCommand_TogglesVisibilityAndActionText()
    {
        Browser browser = new(showGraph: true);
        browser.Load([CreateCommit("child")]);

        Assert.AreEqual("HideCommitGraphMenuFlyoutItemText", browser.CommitGraphToggleText);

        browser.ToggleCommitGraphCommand.Execute(null);

        Assert.IsFalse(browser.IsCommitGraphVisible);
        Assert.AreEqual("ShowCommitGraphMenuFlyoutItemText", browser.CommitGraphToggleText);

        browser.ToggleCommitGraphCommand.Execute(null);

        Assert.IsTrue(browser.IsCommitGraphVisible);
        Assert.AreEqual("HideCommitGraphMenuFlyoutItemText", browser.CommitGraphToggleText);
    }

    private static GitCommit CreateCommit(string hash, params string[] parents) =>
        new(hash, hash, "Author", "author@example.invalid", null, hash, hash, parentHashes: parents);

    private sealed class Browser : CommitBrowserViewModelBase
    {
        private readonly bool _showGraph;

        public Browser(bool showGraph)
            : base(
                null!,
                Stub.Create<IGitService>((method, _) => method == "ExecuteAsync"
                    ? Task.CompletedTask
                    : throw new NotSupportedException(method)),
                Stub.Create<ILocalizationService>((method, args) => method == "GetString"
                    ? args![0]
                    : throw new NotSupportedException(method)),
                null!, null!, null!, new StrongReferenceMessenger(), "none", "file", "repo")
        {
            _showGraph = showGraph;
        }

        protected override bool ShowsCommitGraph => _showGraph;
        protected override bool IsCommitDetailsOperationRunning => false;
        protected override void ShowCommitDetailsError(string message, string? details = null) { }
        public void Load(IReadOnlyList<GitCommit> commits) => ReplaceCommits(commits);
    }

    public class Stub : DispatchProxy
    {
        private Func<string, object?[]?, object?> _invoke = (_, _) => null;

        public static T Create<T>(Func<string, object?[]?, object?> invoke) where T : class
        {
            T instance = Create<T, Stub>();
            ((Stub)(object)instance)._invoke = invoke;
            return instance;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            _invoke(targetMethod!.Name, args);
    }
}
