using System.Collections.Generic;

namespace SimpleGit11.Presentation.Commits;

public enum CommitGraphEndpoint
{
    Top,
    Node,
    Bottom
}

public sealed record CommitGraphSegment(
    int StartLane,
    CommitGraphEndpoint Start,
    int EndLane,
    CommitGraphEndpoint End,
    int ColorIndex,
    bool IsCollapsed);

public sealed record CommitGraphRow(
    int NodeLane,
    int NodeColorIndex,
    int LaneCount,
    IReadOnlyList<CommitGraphSegment> Segments)
{
    public static CommitGraphRow Empty { get; } = new(0, 0, 0, []);
}

public sealed record CommitGraphLayout(
    IReadOnlyDictionary<string, CommitGraphRow> Rows,
    int LaneCount);
