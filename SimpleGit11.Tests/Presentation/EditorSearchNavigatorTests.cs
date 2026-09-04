using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Presentation.Editor;
using TextControlBoxNS;
using TextControlBoxNS.Models;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class EditorSearchNavigatorTests
{
    [TestMethod]
    public void StartSearch_EmptyQuery_ClosesExistingSearchWithoutStartingAnother()
    {
        bool searchEnded = false;
        bool searchStarted = false;

        bool found = EditorSearchNavigator.StartSearch(
            "",
            () => searchEnded = true,
            query =>
            {
                searchStarted = true;
                return SearchResult.Found;
            },
            (line, character) => Assert.Fail("The cursor must not move for an empty query."),
            () => SearchResult.Found);

        Assert.IsFalse(found);
        Assert.IsTrue(searchEnded);
        Assert.IsFalse(searchStarted);
    }

    [TestMethod]
    public void StartSearch_FoundQuery_SelectsFirstMatchFromDocumentStart()
    {
        List<(int Line, int Character)> cursorPositions = [];
        string? startedQuery = null;

        bool found = EditorSearchNavigator.StartSearch(
            "needle",
            () => { },
            query =>
            {
                startedQuery = query;
                return SearchResult.Found;
            },
            (line, character) => cursorPositions.Add((line, character)),
            () => SearchResult.Found);

        Assert.IsTrue(found);
        Assert.AreEqual("needle", startedQuery);
        CollectionAssert.AreEqual(new[] { (0, 0) }, cursorPositions);
    }

    [TestMethod]
    public void StartSearch_MissingQuery_DoesNotMoveCursorOrFindNext()
    {
        bool findNextCalled = false;

        bool found = EditorSearchNavigator.StartSearch(
            "missing",
            () => { },
            query => SearchResult.NotFound,
            (line, character) => Assert.Fail("The cursor must not move when there are no matches."),
            () =>
            {
                findNextCalled = true;
                return SearchResult.Found;
            });

        Assert.IsFalse(found);
        Assert.IsFalse(findNextCalled);
    }

    [TestMethod]
    public void SelectNext_PositionsCursorAfterSelectionBeforeSearching()
    {
        TextControlBoxSelection selection = new()
        {
            StartLinePos = 2,
            StartCharacterPos = 3,
            EndLinePos = 2,
            EndCharacterPos = 9
        };
        List<(int Line, int Character)> cursorPositions = [];

        bool found = EditorSearchNavigator.SelectNext(
            selection,
            (line, character) => cursorPositions.Add((line, character)),
            () => SearchResult.Found);

        Assert.IsTrue(found);
        CollectionAssert.AreEqual(new[] { (2, 9) }, cursorPositions);
    }

    [TestMethod]
    public void SelectNext_ReachedEnd_WrapsToDocumentStart()
    {
        Queue<SearchResult> results = new([SearchResult.ReachedEnd, SearchResult.Found]);
        List<(int Line, int Character)> cursorPositions = [];

        bool found = EditorSearchNavigator.SelectNext(
            null,
            (line, character) => cursorPositions.Add((line, character)),
            results.Dequeue);

        Assert.IsTrue(found);
        CollectionAssert.AreEqual(new[] { (0, 0) }, cursorPositions);
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void SelectPrevious_PositionsCursorBeforeSelectionBeforeSearching()
    {
        TextControlBoxSelection selection = new()
        {
            StartLinePos = 4,
            StartCharacterPos = 5,
            EndLinePos = 4,
            EndCharacterPos = 11
        };
        List<(int Line, int Character)> cursorPositions = [];

        bool found = EditorSearchNavigator.SelectPrevious(
            selection,
            numberOfLines: 8,
            line => 20,
            (line, character) => cursorPositions.Add((line, character)),
            () => SearchResult.Found);

        Assert.IsTrue(found);
        CollectionAssert.AreEqual(new[] { (4, 5) }, cursorPositions);
    }

    [TestMethod]
    public void SelectPrevious_ReachedBeginning_WrapsToDocumentEnd()
    {
        Queue<SearchResult> results = new([SearchResult.ReachedBegin, SearchResult.Found]);
        List<(int Line, int Character)> cursorPositions = [];

        bool found = EditorSearchNavigator.SelectPrevious(
            null,
            numberOfLines: 3,
            line => line == 2 ? 17 : 0,
            (line, character) => cursorPositions.Add((line, character)),
            results.Dequeue);

        Assert.IsTrue(found);
        CollectionAssert.AreEqual(new[] { (2, 17) }, cursorPositions);
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void SelectPrevious_EmptyDocument_DoesNotAttemptWraparound()
    {
        int cursorMoves = 0;

        bool found = EditorSearchNavigator.SelectPrevious(
            null,
            numberOfLines: 0,
            line =>
            {
                Assert.Fail("No line length should be requested for an empty document.");
                return 0;
            },
            (line, character) => cursorMoves++,
            () => SearchResult.ReachedBegin);

        Assert.IsFalse(found);
        Assert.AreEqual(0, cursorMoves);
    }
}
