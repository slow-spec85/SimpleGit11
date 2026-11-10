using System.Globalization;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Tests.Presentation;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class DiffSearchViewModelTests
{
    [TestMethod]
    public async Task Search_StartsAtSelectedFileAndArrowsWrapInFileOrder()
    {
        GitChangedFile first = new("a.txt", "Modified");
        GitChangedFile second = new("b.txt", "Modified");
        GitChangedFile selected = second;
        List<(string Path, int Line)> visited = [];
        StrongReferenceMessenger messenger = new();
        List<AppOperationMessage> operations = [];
        List<AppNotificationMessage> notifications = [];
        messenger.Register<AppOperationMessage>(this, (_, message) => operations.Add(message));
        messenger.Register<AppNotificationMessage>(this, (_, message) => notifications.Add(message));
        ConnectionTestLocalization localization = new();
        DiffSearchViewModel search = new(localization, messenger, false,
            (_, _, _, _) => Task.FromResult<IReadOnlyList<GitFileSearchResult>>(
                [new(null, first, [new(1, 0, 1)]), new(null, second, [new(1, 0, 1), new(2, 0, 1)])]),
            () => (null, selected),
            (result, match, _) =>
            {
                selected = result.File;
                visited.Add((selected.Path, match.Line));
                return Task.FromResult<DiffLine?>(new("x", DiffLineKind.Context));
            },
            () => { });

        search.Query = "x";
        await search.RefreshAsync();
        await search.MoveAsync(forward: true);
        await search.MoveAsync(forward: true);
        await search.MoveAsync(forward: true);
        await search.MoveAsync(forward: false);

        CollectionAssert.AreEqual(new[] { ("b.txt", 1), ("b.txt", 2), ("a.txt", 1), ("b.txt", 1), ("a.txt", 1) }, visited);
        CollectionAssert.AreEqual(new[] { true, false }, operations.Select(operation => operation.IsRunning).ToArray());
        AppNotificationMessage result = notifications.Single();
        Assert.AreEqual(AppNotificationSeverity.Informational, result.Severity);
        Assert.AreEqual(
            string.Format(CultureInfo.CurrentCulture, localization.GetString("DiffFileSearchMatchesFound"), 3),
            result.Message);
    }

    [TestMethod]
    public async Task Search_SeparatesFileVersionsAndClearingRestoresTheList()
    {
        GitChangedFile staged = new("same.txt", "Modified", state: GitChangeState.Staged);
        GitChangedFile working = new("same.txt", "Modified", state: GitChangeState.Unstaged);
        GitChangedFile? selected = working;
        DiffSearchViewModel search = new(new ConnectionTestLocalization(), new StrongReferenceMessenger(), false,
            (_, _, _, _) => Task.FromResult<IReadOnlyList<GitFileSearchResult>>([new(null, staged, [new(1, 0, 1)])]),
            () => (null, selected),
            (result, _, _) =>
            {
                selected = result.File;
                return Task.FromResult<DiffLine?>(new("x", DiffLineKind.Context));
            },
            () => { });

        search.Query = "x";
        await search.RefreshAsync();
        Assert.AreSame(staged, selected);
        Assert.IsFalse(search.Includes(null, working));

        search.Query = "";
        Assert.IsTrue(search.Includes(null, working));
        Assert.IsFalse(search.CanNavigate);
        Assert.IsNull(search.Selection);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Search_GlobalCancelStopsSearchWithoutApplyingLateResults(bool searchHistory, bool changedLinesOnly)
    {
        StrongReferenceMessenger messenger = new();
        List<AppOperationMessage> operations = [];
        List<AppNotificationMessage> notifications = [];
        messenger.Register<AppOperationMessage>(this, (_, message) => operations.Add(message));
        messenger.Register<AppNotificationMessage>(this, (_, message) => notifications.Add(message));
        ConnectionTestLocalization localization = new();
        GitChangedFile file = new("file.txt", "Modified");
        TaskCompletionSource<CancellationToken> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<IReadOnlyList<GitFileSearchResult>> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int searches = 0;
        int filterUpdates = 0;
        int openedMatches = 0;
        DiffSearchViewModel search = new(localization, messenger, true,
            (_, changed, history, token) =>
            {
                Assert.AreEqual(searchHistory, history);
                Assert.AreEqual(changedLinesOnly, changed);
                if (++searches == 1)
                {
                    started.SetResult(token);
                    return finished.Task;
                }
                return Task.FromResult<IReadOnlyList<GitFileSearchResult>>([]);
            },
            () => (null, file),
            (_, _, _) =>
            {
                openedMatches++;
                return Task.FromResult<DiffLine?>(null);
            },
            () => filterUpdates++);

        search.SearchHistory = searchHistory;
        search.ChangedLinesOnly = changedLinesOnly;
        search.Query = "text";
        Task pending = search.RefreshAsync();
        CancellationToken token = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        AppOperationMessage running = operations.Single();
        Assert.IsTrue(running.IsRunning);
        Assert.AreEqual(localization.GetString(searchHistory ? "DiffHistorySearchRunning" : "DiffFileSearchRunning"),
            running.Message);
        Assert.IsEmpty(notifications);
        Assert.IsNotNull(running.CancelCommand);
        running.CancelCommand.Execute(null);
        Assert.IsTrue(token.IsCancellationRequested);
        Assert.IsFalse(operations.Last().IsRunning);
        Assert.AreSame(running.Source, operations.Last().Source);
        Assert.IsFalse(search.IsBusy);
        Assert.AreEqual("text", search.Query);

        finished.SetResult([new(null, file, [new(1, 0, 4)])]);
        await pending;
        await search.RefreshAsync();
        Assert.AreEqual(1, searches);
        Assert.AreEqual(0, filterUpdates);
        Assert.AreEqual(0, openedMatches);
        AppNotificationMessage canceled = notifications.Single();
        Assert.AreEqual(AppNotificationSeverity.Informational, canceled.Severity);
        Assert.AreEqual(localization.GetString("DiffFileSearchCanceled"), canceled.Message);

        Assert.IsTrue(search.RetryCanceledSearch());
        await search.RefreshAsync();
        Assert.AreEqual(2, searches);
        Assert.AreEqual(1, filterUpdates);
        Assert.IsFalse(operations.Last().IsRunning);
        Assert.HasCount(2, notifications);
        Assert.AreEqual(AppNotificationSeverity.Informational, notifications.Last().Severity);
        Assert.AreEqual(localization.GetString("DiffFileSearchNoMatches"), notifications.Last().Message);
    }

    [TestMethod]
    public void FindLine_UsesOldLineNumbersForADeletedFile()
    {
        GitChangedFile deleted = new("deleted.txt", "Deleted");
        DiffLine first = new("x", DiffLineKind.Removed, sourceLineNumber: 1, oldLineNumber: 1);
        DiffLine second = new("x", DiffLineKind.Removed, sourceLineNumber: 1, oldLineNumber: 2);

        Assert.AreSame(second, DiffSearchViewModel.FindLine([first, second], deleted, new(2, 0, 1), "x"));
    }

    [TestMethod]
    public async Task ResetFilters_ClearsModesAndRestoresFilesWhileCancelingHistorySearch()
    {
        StrongReferenceMessenger messenger = new();
        List<AppOperationMessage> operations = [];
        messenger.Register<AppOperationMessage>(this, (_, message) => operations.Add(message));
        GitChangedFile matched = new("matched.txt", "Modified");
        GitChangedFile hidden = new("hidden.txt", "Modified");
        GitFileSearchResult[] matches = [new(null, matched, [new(1, 0, 1)])];
        TaskCompletionSource<CancellationToken> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<IReadOnlyList<GitFileSearchResult>> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int filterUpdates = 0;
        DiffSearchViewModel search = new(new ConnectionTestLocalization(), messenger, true,
            (query, _, _, token) =>
            {
                if (query == "x") { return Task.FromResult<IReadOnlyList<GitFileSearchResult>>(matches); }
                started.SetResult(token);
                return finished.Task;
            },
            () => (null, matched),
            (_, _, _) => Task.FromResult<DiffLine?>(new("x", DiffLineKind.Context)),
            () => filterUpdates++);

        Assert.IsFalse(search.HasFilters);
        search.ChangedLinesOnly = true;
        Assert.IsTrue(search.HasFilters);
        search.SearchHistory = true;
        search.Query = "x";
        await search.RefreshAsync();
        Assert.IsFalse(search.Includes(null, hidden));
        Assert.IsNotNull(search.Selection);

        search.Query = "y";
        Task pending = search.RefreshAsync();
        CancellationToken token = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        search.ResetFilters();
        finished.SetResult(matches);
        await pending;

        Assert.AreEqual("", search.Query);
        Assert.IsFalse(search.ChangedLinesOnly);
        Assert.IsFalse(search.SearchHistory);
        Assert.IsFalse(search.HasFilters);
        Assert.IsFalse(search.CanNavigate);
        Assert.IsNull(search.Selection);
        Assert.IsTrue(search.Includes(null, hidden));
        Assert.AreEqual(2, filterUpdates);
        Assert.IsTrue(token.IsCancellationRequested);
        Assert.IsFalse(operations.Last().IsRunning);
    }
}
