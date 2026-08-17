using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public static partial class DiffTextFormatter
{
    public static string FormatText(IReadOnlyList<DiffLine> lines)
    {
        return string.Join(Environment.NewLine, lines.Select(line => line.Text));
    }

    public static IReadOnlyList<DiffLine> FormatFullFile(
        string text,
        IReadOnlyList<DiffLine> diffLines,
        bool useOldLineNumbers)
    {
        List<string> fileLines = SplitLines(text);
        if (diffLines.Any(line => line.Kind == DiffLineKind.ConflictMarker))
        {
            return FormatConflictFile(fileLines);
        }

        List<DiffChangeBlock> blocks = ParseChangeBlocks(diffLines);
        return useOldLineNumbers
            ? FormatOldFile(fileLines, blocks)
            : FormatNewFile(fileLines, blocks);
    }

    public static IReadOnlyList<DiffLine> FormatEditableFile(
        string text,
        IReadOnlyList<DiffLine> diffLines)
    {
        return FormatFullFile(text, diffLines, useOldLineNumbers: false)
            .Where(line => line.Kind != DiffLineKind.Removed)
            .ToArray();
    }

    private static IReadOnlyList<DiffLine> FormatConflictFile(IReadOnlyList<string> fileLines)
    {
        List<DiffLine> lines = [];
        for (int index = 0; index < fileLines.Count; index++)
        {
            string text = fileLines[index];
            DiffLineKind kind = GitConflictMarkerDetector.IsMarker(text)
                ? DiffLineKind.ConflictMarker
                : DiffLineKind.Context;
            int lineNumber = index + 1;
            lines.Add(new DiffLine(text, kind, sourceLineNumber: lineNumber, displayLineNumber: lineNumber));
        }

        return lines;
    }

    private static IReadOnlyList<DiffLine> FormatNewFile(IReadOnlyList<string> fileLines, IReadOnlyList<DiffChangeBlock> blocks)
    {
        List<DiffLine> lines = [];
        var blocksByStart = blocks
            .GroupBy(block => block.NewStart)
            .ToDictionary(group => group.Key, group => group.ToList());

        for (int lineNumber = 1; lineNumber <= fileLines.Count; lineNumber++)
        {
            if (blocksByStart.TryGetValue(lineNumber, out List<DiffChangeBlock>? startingBlocks))
            {
                foreach (DiffChangeBlock block in startingBlocks)
                {
                    foreach (DiffLine oldLine in block.OldLines)
                    {
                        lines.Add(new DiffLine(
                            oldLine.Text,
                            DiffLineKind.Removed,
                            "-",
                            Math.Max(1, block.NewStart),
                            inlineSegments: oldLine.InlineSegments));
                    }
                }
            }

            DiffChangeBlock? addedBlock = blocks.FirstOrDefault(block =>
                block.NewLineCount > 0 &&
                lineNumber >= block.NewStart &&
                lineNumber < block.NewStart + block.NewLineCount);
            DiffLineKind kind = addedBlock is null ? DiffLineKind.Context : DiffLineKind.Added;
            string marker = kind == DiffLineKind.Added ? "+" : "";
            IReadOnlyList<DiffLineSegment> inlineSegments = addedBlock?.GetAddedInlineSegments(lineNumber) ?? [];
            lines.Add(new DiffLine(fileLines[lineNumber - 1], kind, marker, lineNumber, lineNumber, inlineSegments));
        }

        if (blocksByStart.TryGetValue(fileLines.Count + 1, out List<DiffChangeBlock>? trailingBlocks))
        {
            foreach (DiffChangeBlock block in trailingBlocks)
            {
                foreach (DiffLine oldLine in block.OldLines)
                {
                    lines.Add(new DiffLine(
                        oldLine.Text,
                        DiffLineKind.Removed,
                        "-",
                        Math.Max(1, block.NewStart),
                        inlineSegments: oldLine.InlineSegments));
                }
            }
        }

        return lines;
    }

    private static IReadOnlyList<DiffLine> FormatOldFile(IReadOnlyList<string> fileLines, IReadOnlyList<DiffChangeBlock> blocks)
    {
        List<DiffLine> lines = [];

        for (int lineNumber = 1; lineNumber <= fileLines.Count; lineNumber++)
        {
            DiffChangeBlock? removedBlock = blocks.FirstOrDefault(block =>
                block.OldLineCount > 0 &&
                lineNumber >= block.OldStart &&
                lineNumber < block.OldStart + block.OldLineCount);
            DiffLineKind kind = removedBlock is null ? DiffLineKind.Context : DiffLineKind.Removed;
            string marker = kind == DiffLineKind.Removed ? "-" : "";
            int? displayLineNumber = kind == DiffLineKind.Removed ? null : lineNumber;
            IReadOnlyList<DiffLineSegment> inlineSegments = removedBlock?.GetOldInlineSegments(lineNumber) ?? [];
            lines.Add(new DiffLine(fileLines[lineNumber - 1], kind, marker, lineNumber, displayLineNumber, inlineSegments));
        }

        return lines;
    }

    private static List<DiffChangeBlock> ParseChangeBlocks(IReadOnlyList<DiffLine> diffLines)
    {
        List<DiffChangeBlock> blocks = [];
        int oldLineNumber = 0;
        int newLineNumber = 0;
        int blockOldStart = 0;
        int blockNewStart = 0;
        int blockOldLineCount = 0;
        int blockNewLineCount = 0;
        bool blockStarted = false;
        List<DiffLine> blockOldLines = [];
        List<DiffLine> blockAddedLines = [];

        foreach (DiffLine line in diffLines)
        {
            if (TryReadHunk(line.Text, out int oldStart, out int newStart))
            {
                FlushBlock();
                oldLineNumber = oldStart;
                newLineNumber = newStart;
                continue;
            }

            if (line.Kind == DiffLineKind.Added)
            {
                EnsureBlockStarted();
                blockNewLineCount++;
                blockAddedLines.Add(line);
                newLineNumber++;
            }
            else if (line.Kind == DiffLineKind.Removed)
            {
                EnsureBlockStarted();
                blockOldLineCount++;
                blockOldLines.Add(line);
                oldLineNumber++;
            }
            else
            {
                FlushBlock();
                if (line.Kind == DiffLineKind.Context)
                {
                    oldLineNumber++;
                    newLineNumber++;
                }
            }
        }

        FlushBlock();
        return blocks;

        void EnsureBlockStarted()
        {
            if (blockStarted)
            {
                return;
            }

            blockStarted = true;
            blockOldStart = oldLineNumber;
            blockNewStart = newLineNumber;
            blockOldLineCount = 0;
            blockNewLineCount = 0;
            blockOldLines = [];
            blockAddedLines = [];
        }

        void FlushBlock()
        {
            if (!blockStarted)
            {
                return;
            }

            blocks.Add(new DiffChangeBlock(
                blockOldStart,
                blockNewStart,
                blockOldLineCount,
                blockNewLineCount,
                blockOldLines,
                blockAddedLines));
            blockStarted = false;
            blockOldStart = 0;
            blockNewStart = 0;
            blockOldLineCount = 0;
            blockNewLineCount = 0;
            blockOldLines = [];
            blockAddedLines = [];
        }
    }

    private static List<string> SplitLines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();
    }

    private static bool TryReadHunk(string text, out int oldStart, out int newStart)
    {
        oldStart = 0;
        newStart = 0;
        Match match = HunkHeaderRegex().Match(text);
        if (!match.Success)
        {
            return false;
        }

        oldStart = int.Parse(match.Groups["old"].Value);
        newStart = int.Parse(match.Groups["new"].Value);
        return true;
    }

    [GeneratedRegex("^@@ -(?'old'\\d+)(?:,\\d+)? \\+(?'new'\\d+)(?:,\\d+)? @@")]
    private static partial Regex HunkHeaderRegex();

    private sealed class DiffChangeBlock
    {
        public DiffChangeBlock(
            int oldStart,
            int newStart,
            int oldLineCount,
            int newLineCount,
            IReadOnlyList<DiffLine> oldLines,
            IReadOnlyList<DiffLine> addedLines)
        {
            OldStart = oldStart;
            NewStart = newStart;
            OldLineCount = oldLineCount;
            NewLineCount = newLineCount;
            OldLines = oldLines;
            AddedLines = addedLines;
        }

        public int OldStart { get; }

        public int NewStart { get; }

        public int OldLineCount { get; }

        public int NewLineCount { get; }

        public IReadOnlyList<DiffLine> OldLines { get; }

        public IReadOnlyList<DiffLine> AddedLines { get; }

        public IReadOnlyList<DiffLineSegment> GetAddedInlineSegments(int lineNumber)
        {
            int index = lineNumber - NewStart;
            return index >= 0 && index < AddedLines.Count
                ? AddedLines[index].InlineSegments
                : [];
        }

        public IReadOnlyList<DiffLineSegment> GetOldInlineSegments(int lineNumber)
        {
            int index = lineNumber - OldStart;
            return index >= 0 && index < OldLines.Count
                ? OldLines[index].InlineSegments
                : [];
        }
    }
}
