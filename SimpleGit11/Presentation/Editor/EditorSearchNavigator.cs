using System;
using TextControlBoxNS;
using TextControlBoxNS.Models;

namespace SimpleGit11.Presentation.Editor;

internal static class EditorSearchNavigator
{
    public static bool StartSearch(
        string query,
        Action endSearch,
        Func<string, SearchResult> beginSearch,
        Action<int, int> setCursorPosition,
        Func<SearchResult> findNext)
    {
        endSearch();
        if (string.IsNullOrEmpty(query) || beginSearch(query) != SearchResult.Found)
        {
            return false;
        }

        setCursorPosition(0, 0);
        return findNext() == SearchResult.Found;
    }

    public static bool SelectNext(
        TextControlBoxSelection? selection,
        Action<int, int> setCursorPosition,
        Func<SearchResult> findNext)
    {
        if (selection is TextControlBoxSelection currentSelection)
        {
            setCursorPosition(
                currentSelection.EndLinePos,
                currentSelection.EndCharacterPos);
        }

        SearchResult result = findNext();
        if (result == SearchResult.Found)
        {
            return true;
        }

        if (result != SearchResult.ReachedEnd)
        {
            return false;
        }

        setCursorPosition(0, 0);
        return findNext() == SearchResult.Found;
    }

    public static bool SelectPrevious(
        TextControlBoxSelection? selection,
        int numberOfLines,
        Func<int, int> getLineLength,
        Action<int, int> setCursorPosition,
        Func<SearchResult> findPrevious)
    {
        if (selection is TextControlBoxSelection currentSelection)
        {
            setCursorPosition(
                currentSelection.StartLinePos,
                currentSelection.StartCharacterPos);
        }

        SearchResult result = findPrevious();
        if (result == SearchResult.Found)
        {
            return true;
        }

        if (result != SearchResult.ReachedBegin || numberOfLines == 0)
        {
            return false;
        }

        int lastLine = numberOfLines - 1;
        setCursorPosition(lastLine, getLineLength(lastLine));
        return findPrevious() == SearchResult.Found;
    }
}
