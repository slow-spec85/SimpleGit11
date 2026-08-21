using System;
using System.Collections.Generic;
using System.Linq;
using SimpleGit11.Models;

namespace SimpleGit11.Presentation.Editor;

internal sealed class DiffEditorProjection
{
    private DiffEditorProjection(
        IReadOnlyList<DiffLine> sourceLines,
        IReadOnlyList<string> lines,
        IReadOnlyList<DiffEditorLineBlock> lineBlocks,
        IReadOnlyList<DiffEditorTextRange> textRanges,
        IReadOnlyList<int> syntaxStateBoundaryLines)
    {
        SourceLines = sourceLines;
        Lines = lines;
        LineBlocks = lineBlocks;
        TextRanges = textRanges;
        SyntaxStateBoundaryLines = syntaxStateBoundaryLines;
    }

    public IReadOnlyList<DiffLine> SourceLines { get; }

    public IReadOnlyList<string> Lines { get; }

    public IReadOnlyList<DiffEditorLineBlock> LineBlocks { get; }

    public IReadOnlyList<DiffEditorTextRange> TextRanges { get; }

    public IReadOnlyList<int> SyntaxStateBoundaryLines { get; }

    public static DiffEditorProjection Create(IEnumerable<DiffLine>? source)
    {
        IReadOnlyList<DiffLine> sourceLines = source?.ToArray() ?? [];
        List<DiffEditorLineBlock> lineBlocks = [];
        List<DiffEditorTextRange> textRanges = [];
        List<int> syntaxStateBoundaryLines = [];
        int blockStart = -1;
        DiffLineKind blockKind = DiffLineKind.Context;

        for (int index = 0; index <= sourceLines.Count; index++)
        {
            DiffLineKind kind = index < sourceLines.Count
                ? sourceLines[index].Kind
                : DiffLineKind.Context;
            bool hasBackground = HasBackground(kind);

            if (blockStart >= 0 && (!hasBackground || kind != blockKind))
            {
                lineBlocks.Add(new DiffEditorLineBlock(blockStart, index - 1, blockKind));
                blockStart = -1;
            }

            if (blockStart < 0 && hasBackground)
            {
                blockStart = index;
                blockKind = kind;
            }

            if (index >= sourceLines.Count)
            {
                continue;
            }

            DiffLine line = sourceLines[index];
            if (line.Kind is DiffLineKind.Header or DiffLineKind.Hunk or DiffLineKind.ConflictMarker)
            {
                syntaxStateBoundaryLines.Add(index);
            }

            foreach (DiffLineSegment segment in line.InlineSegments)
            {
                int start = Math.Clamp(segment.StartIndex, 0, line.Text.Length);
                int length = Math.Clamp(segment.Length, 0, line.Text.Length - start);
                if (length > 0)
                {
                    textRanges.Add(new DiffEditorTextRange(index, start, length, line.Kind));
                }
            }
        }

        return new DiffEditorProjection(
            sourceLines,
            sourceLines.Select(line => line.Text).ToArray(),
            lineBlocks,
            textRanges,
            syntaxStateBoundaryLines);
    }

    private static bool HasBackground(DiffLineKind kind)
    {
        return kind is DiffLineKind.Added
            or DiffLineKind.Removed
            or DiffLineKind.Header
            or DiffLineKind.Hunk
            or DiffLineKind.ConflictMarker;
    }
}

internal sealed record DiffEditorLineBlock(
    int StartLine,
    int EndLine,
    DiffLineKind Kind);

internal sealed record DiffEditorTextRange(
    int Line,
    int StartColumn,
    int Length,
    DiffLineKind Kind);
