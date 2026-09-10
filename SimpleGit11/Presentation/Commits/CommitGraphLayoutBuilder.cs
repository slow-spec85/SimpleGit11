using System;
using System.Collections.Generic;
using System.Linq;
using SimpleGit11.Models;

namespace SimpleGit11.Presentation.Commits;

public static class CommitGraphLayoutBuilder
{
    private const int PaletteSize = 6;

    public static CommitGraphLayout Build(
        IReadOnlyList<GitCommit> commits,
        IReadOnlySet<string>? visibleHashes = null,
        bool firstParentOnly = false)
    {
        HashSet<string> visible = visibleHashes is null
            ? commits.Select(commit => commit.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(visibleHashes, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, GitCommit> commitsByHash = commits.ToDictionary(
            commit => commit.Hash,
            StringComparer.OrdinalIgnoreCase);
        List<LaneState> activeLanes = [];
        Dictionary<string, MutableGraphRow> rows = new(StringComparer.OrdinalIgnoreCase);
        int nextColorIndex = 0;
        int maximumLaneCount = 0;

        foreach (GitCommit commit in commits.Where(commit => visible.Contains(commit.Hash)))
        {
            List<LaneState> before = [.. activeLanes];
            int nodeLane = FindLane(before, commit.Hash);
            bool hasIncomingLine = nodeLane >= 0;
            if (!hasIncomingLine)
            {
                nodeLane = 0;
                before.Insert(nodeLane, new LaneState(commit.Hash, NextColor(ref nextColorIndex), false));
            }

            LaneState nodeState = before[nodeLane];
            IReadOnlyList<EffectiveParent> parents = GetEffectiveParents(
                commit,
                commitsByHash,
                visible,
                firstParentOnly);
            List<LaneState> after = before
                .Where((_, index) => index != nodeLane)
                .ToList();

            for (int parentIndex = 0; parentIndex < parents.Count; parentIndex++)
            {
                EffectiveParent parent = parents[parentIndex];
                if (FindLane(after, parent.Hash) >= 0)
                {
                    continue;
                }

                int insertionIndex = Math.Min(nodeLane + parentIndex, after.Count);
                int colorIndex = parentIndex == 0
                    ? nodeState.ColorIndex
                    : NextColor(ref nextColorIndex);
                after.Insert(
                    insertionIndex,
                    new LaneState(parent.Hash, colorIndex, parent.IsCollapsed));
            }

            List<CommitGraphSegment> segments = [];
            for (int lane = 0; lane < before.Count; lane++)
            {
                LaneState incoming = before[lane];
                if (lane == nodeLane)
                {
                    if (hasIncomingLine)
                    {
                        segments.Add(new CommitGraphSegment(
                            lane,
                            CommitGraphEndpoint.Top,
                            nodeLane,
                            CommitGraphEndpoint.Node,
                            incoming.ColorIndex,
                            incoming.IsCollapsed));
                    }

                    continue;
                }

                int continuingLane = FindLane(after, incoming.TargetHash);
                if (continuingLane >= 0)
                {
                    segments.Add(new CommitGraphSegment(
                        lane,
                        CommitGraphEndpoint.Top,
                        continuingLane,
                        CommitGraphEndpoint.Bottom,
                        incoming.ColorIndex,
                        incoming.IsCollapsed));
                }
            }

            foreach (EffectiveParent parent in parents)
            {
                int parentLane = FindLane(after, parent.Hash);
                LaneState parentState = after[parentLane];
                segments.Add(new CommitGraphSegment(
                    nodeLane,
                    CommitGraphEndpoint.Node,
                    parentLane,
                    CommitGraphEndpoint.Bottom,
                    parentState.ColorIndex,
                    parent.IsCollapsed));
            }

            int rowLaneCount = Math.Max(before.Count, after.Count);
            maximumLaneCount = Math.Max(maximumLaneCount, rowLaneCount);
            rows[commit.Hash] = new MutableGraphRow(
                nodeLane,
                nodeState.ColorIndex,
                rowLaneCount,
                segments);
            activeLanes = after;
        }

        Dictionary<string, CommitGraphRow> completedRows = rows.ToDictionary(
            item => item.Key,
            item => new CommitGraphRow(
                item.Value.NodeLane,
                item.Value.NodeColorIndex,
                maximumLaneCount,
                item.Value.Segments),
            StringComparer.OrdinalIgnoreCase);
        return new CommitGraphLayout(completedRows, maximumLaneCount);
    }

    private static IReadOnlyList<EffectiveParent> GetEffectiveParents(
        GitCommit commit,
        IReadOnlyDictionary<string, GitCommit> commitsByHash,
        IReadOnlySet<string> visibleHashes,
        bool firstParentOnly)
    {
        IEnumerable<string> parentHashes = firstParentOnly
            ? commit.ParentHashes.Take(1)
            : commit.ParentHashes;
        List<EffectiveParent> result = [];
        HashSet<string> resultHashes = new(StringComparer.OrdinalIgnoreCase);

        foreach (string parentHash in parentHashes)
        {
            CollectVisibleParents(
                parentHash,
                false,
                commitsByHash,
                visibleHashes,
                firstParentOnly,
                result,
                resultHashes);
        }

        return result;
    }

    private static void CollectVisibleParents(
        string initialHash,
        bool isCollapsed,
        IReadOnlyDictionary<string, GitCommit> commitsByHash,
        IReadOnlySet<string> visibleHashes,
        bool firstParentOnly,
        ICollection<EffectiveParent> result,
        ISet<string> resultHashes)
    {
        Stack<(string Hash, bool IsCollapsed)> pending = [];
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        pending.Push((initialHash, isCollapsed));

        while (pending.Count > 0)
        {
            (string hash, bool collapsed) = pending.Pop();
            if (!visited.Add(hash))
            {
                continue;
            }

            if (visibleHashes.Contains(hash) || !commitsByHash.TryGetValue(hash, out GitCommit? hiddenCommit))
            {
                if (resultHashes.Add(hash))
                {
                    result.Add(new EffectiveParent(hash, collapsed));
                }

                continue;
            }

            IReadOnlyList<string> hiddenParents = firstParentOnly
                ? hiddenCommit.ParentHashes.Take(1).ToArray()
                : hiddenCommit.ParentHashes;
            if (hiddenParents.Count == 0)
            {
                if (resultHashes.Add(hash))
                {
                    result.Add(new EffectiveParent(hash, true));
                }

                continue;
            }

            for (int index = hiddenParents.Count - 1; index >= 0; index--)
            {
                pending.Push((hiddenParents[index], true));
            }
        }
    }

    private static int FindLane(IReadOnlyList<LaneState> lanes, string hash)
    {
        for (int index = 0; index < lanes.Count; index++)
        {
            if (string.Equals(lanes[index].TargetHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static int NextColor(ref int nextColorIndex)
    {
        int colorIndex = nextColorIndex % PaletteSize;
        nextColorIndex++;
        return colorIndex;
    }

    private sealed record LaneState(string TargetHash, int ColorIndex, bool IsCollapsed);

    private sealed record EffectiveParent(string Hash, bool IsCollapsed);

    private sealed record MutableGraphRow(
        int NodeLane,
        int NodeColorIndex,
        int LaneCount,
        IReadOnlyList<CommitGraphSegment> Segments);
}
