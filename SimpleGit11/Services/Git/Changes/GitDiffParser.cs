using System;
using System.Collections.Generic;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

internal static class GitDiffParser
{
    public static IReadOnlyList<DiffLine> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        List<DiffLine> lines = [];
        int oldLineNumber = 0;
        int newLineNumber = 0;
        bool isInsideHunk = false;
        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                lines.Add(new DiffLine(line, DiffLineKind.Hunk));
                isInsideHunk = TryReadHunkStart(line, out oldLineNumber, out newLineNumber);
            }
            else if (IsHeader(line))
            {
                lines.Add(new DiffLine(line, DiffLineKind.Header));
            }
            else if (line.StartsWith('+'))
            {
                int? sourceLineNumber = isInsideHunk ? Math.Max(1, newLineNumber) : null;
                lines.Add(new DiffLine(
                    line[1..],
                    DiffLineKind.Added,
                    "+",
                    sourceLineNumber: sourceLineNumber));
                newLineNumber++;
            }
            else if (line.StartsWith('-'))
            {
                int? sourceLineNumber = isInsideHunk ? Math.Max(1, newLineNumber) : null;
                lines.Add(new DiffLine(
                    line[1..],
                    DiffLineKind.Removed,
                    "-",
                    sourceLineNumber: sourceLineNumber));
                oldLineNumber++;
            }
            else
            {
                string text = line.StartsWith(' ') ? line[1..] : line;
                int? sourceLineNumber = isInsideHunk ? Math.Max(1, newLineNumber) : null;
                lines.Add(new DiffLine(
                    text,
                    DiffLineKind.Context,
                    sourceLineNumber: sourceLineNumber));
                if (isInsideHunk && !line.StartsWith("\\ No newline ", StringComparison.Ordinal))
                {
                    oldLineNumber++;
                    newLineNumber++;
                }
            }
        }

        ApplyInlineChangeHighlights(lines);
        return lines;
    }

    private static bool TryReadHunkStart(string line, out int oldStart, out int newStart)
    {
        oldStart = 0;
        newStart = 0;
        string[] headerParts = line.Split(' ');
        return headerParts.Length >= 3
            && TryReadHunkLineNumber(headerParts[1], '-', out oldStart)
            && TryReadHunkLineNumber(headerParts[2], '+', out newStart);
    }

    private static bool TryReadHunkLineNumber(string part, char prefix, out int lineNumber)
    {
        lineNumber = 0;
        if (part.Length < 2 || part[0] != prefix)
        {
            return false;
        }

        int commaIndex = part.IndexOf(',');
        string value = commaIndex >= 0 ? part[1..commaIndex] : part[1..];
        return int.TryParse(value, out lineNumber);
    }

    private static bool IsHeader(string line)
    {
        return line.StartsWith("diff --git", StringComparison.Ordinal)
            || line.StartsWith("index ", StringComparison.Ordinal)
            || line.StartsWith("---", StringComparison.Ordinal)
            || line.StartsWith("+++", StringComparison.Ordinal)
            || line.StartsWith("new file mode", StringComparison.Ordinal)
            || line.StartsWith("deleted file mode", StringComparison.Ordinal)
            || line.StartsWith("\\ No newline ", StringComparison.Ordinal);
    }

    private static void ApplyInlineChangeHighlights(IReadOnlyList<DiffLine> lines)
    {
        List<DiffLine> removedLines = [];
        List<DiffLine> addedLines = [];

        foreach (DiffLine line in lines)
        {
            if (line.Kind == DiffLineKind.Removed)
            {
                removedLines.Add(line);
                continue;
            }

            if (line.Kind == DiffLineKind.Added)
            {
                addedLines.Add(line);
                continue;
            }

            FlushChangeBlock();
        }

        FlushChangeBlock();

        void FlushChangeBlock()
        {
            int pairCount = Math.Min(removedLines.Count, addedLines.Count);
            for (int index = 0; index < pairCount; index++)
            {
                ApplyInlineChangeHighlight(removedLines[index], addedLines[index]);
            }

            removedLines.Clear();
            addedLines.Clear();
        }
    }

    private static void ApplyInlineChangeHighlight(DiffLine removedLine, DiffLine addedLine)
    {
        int prefixLength = GetCommonPrefixLength(removedLine.Text, addedLine.Text);
        int suffixLength = GetCommonSuffixLength(removedLine.Text, addedLine.Text, prefixLength);
        int removedLength = removedLine.Text.Length - prefixLength - suffixLength;
        int addedLength = addedLine.Text.Length - prefixLength - suffixLength;

        if (removedLength > 0)
        {
            removedLine.SetInlineSegments([new DiffLineSegment(prefixLength, removedLength)]);
        }

        if (addedLength > 0)
        {
            addedLine.SetInlineSegments([new DiffLineSegment(prefixLength, addedLength)]);
        }
    }

    private static int GetCommonPrefixLength(string oldText, string newText)
    {
        int length = Math.Min(oldText.Length, newText.Length);
        int index = 0;
        while (index < length && oldText[index] == newText[index])
        {
            index++;
        }

        return index;
    }

    private static int GetCommonSuffixLength(string oldText, string newText, int prefixLength)
    {
        int oldIndex = oldText.Length - 1;
        int newIndex = newText.Length - 1;
        int suffixLength = 0;

        while (oldIndex >= prefixLength
            && newIndex >= prefixLength
            && oldText[oldIndex] == newText[newIndex])
        {
            oldIndex--;
            newIndex--;
            suffixLength++;
        }

        return suffixLength;
    }
}
