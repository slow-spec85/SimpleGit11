using System;
using System.Collections.Generic;
using System.Linq;
using SimpleGit11.Models;

namespace SimpleGit11.Presentation.Commits;

public sealed partial class HistoryEditSession
{
    private readonly IReadOnlySet<string> _editable;
    private readonly bool _hasGraph;
    private string _head;

    public bool PreservesMerges => _hasGraph;

    // Return the merge whose parent determines the meaning of this operation.
    public GitCommit? MoveBoundary(string hash, int direction)
    {
        int index = _groups.FindIndex(group => group.Commit.Hash == hash);
        int neighbor = index < 0 ? -1 : Neighbor(index, direction);
        if (neighbor < 0) { return null; }
        if (_groups[index].Commit.IsMerge) { return ParentChoice(_groups[index]); }
        if (_groups[neighbor].Commit.IsMerge) { return ParentChoice(_groups[neighbor]); }
        bool Ancestor(string ancestor, string descendant, HashSet<string> visited)
        {
            if (ancestor == descendant) { return true; }
            if (!visited.Add(descendant) || Find(descendant) is not Group group) { return false; }
            return group.Parents.Any(parent => Ancestor(ancestor, parent, visited));
        }
        foreach (Group merge in _groups.Where(group => !group.Deleted && group.Commit.IsMerge))
        {
            int sourceLine = merge.Parents.FindIndex(parent => Ancestor(hash, parent, []));
            int neighborLine = merge.Parents.FindIndex(parent => Ancestor(_groups[neighbor].Commit.Hash, parent, []));
            if (sourceLine >= 0 && neighborLine >= 0 && sourceLine != neighborLine) { return ParentChoice(merge); }
        }
        return null;
    }

    public GitCommit? MergeBoundary(string hash)
    {
        Group? group = Find(hash);
        if (group?.Commit.IsMerge == true) { return ParentChoice(group); }
        GitCommit? previous = Previous(hash);
        return previous?.IsMerge == true ? ParentChoice(Find(previous.Hash)!) : null;
    }

    public GitCommit? MergeTarget(string hash, int? parentIndex = null)
    {
        GitCommit? boundary = MergeBoundary(hash);
        if (boundary is null || parentIndex is null) { return Previous(hash); }
        Group? merge = Find(boundary.Hash);
        string? parent = merge?.Parents.ElementAtOrDefault(parentIndex.Value - 1);
        return parent is null ? null : ResolveGroup(parent)?.Commit;
    }

    private static GitCommit ParentChoice(Group group) => new(group.Commit.Hash, group.Commit.ShortHash,
        group.Commit.AuthorName, group.Commit.AuthorEmail, group.Commit.AuthoredAt,
        Title(group.Message), group.Message, parentHashes: group.Parents.ToArray());

    private Group? ResolveGroup(string hash, HashSet<string>? visited = null)
    {
        visited ??= [];
        if (!visited.Add(hash)) { throw new InvalidOperationException("The history draft contains a cycle."); }
        Group? group = Find(hash);
        if (group is null) { return null; }
        if (!group.Deleted) { return group; }
        return group.Parents.Count == 0 ? null : ResolveGroup(group.Parents[0], visited);
    }

    private string ResolveParent(string hash)
    {
        Group? group = Find(hash);
        HashSet<string> visited = [];
        while (group?.Deleted == true)
        {
            if (!visited.Add(hash)) { throw new InvalidOperationException("The history draft contains a cycle."); }
            if (group.Parents.Count == 0) { return ""; }
            hash = group.Parents[0];
            group = Find(hash);
        }
        return hash;
    }

    private void ReplaceParent(string oldHash, string newHash, Group? except = null)
    {
        foreach (Group group in _groups.Where(group => !ReferenceEquals(group, except)))
        {
            for (int index = 0; index < group.Parents.Count; index++)
            {
                if (group.Parents[index] == oldHash) { group.Parents[index] = newHash; }
            }
        }
    }

    private void MoveGraph(Group source, Group neighbor, int direction, int? parentIndex)
    {
        source.MoveDirection = direction;
        GitCommit? boundary = MoveBoundary(source.Commit.Hash, direction);
        Group? merge = boundary is null ? null : Find(boundary.Hash);
        if (merge is not null)
        {
            int parent = (parentIndex ?? 1) - 1;
            if (parent < 0 || parent >= merge.Parents.Count) { throw new ArgumentOutOfRangeException(nameof(parentIndex)); }
            Group moving = ReferenceEquals(source, merge)
                ? ResolveGroup(merge.Parents[parent]) ?? neighbor : source;
            if (ReferenceEquals(source, merge)) { neighbor = moving; }
            if (!_editable.Contains(moving.Commit.Hash)) { throw new InvalidOperationException("The selected parent is outside the editable range."); }
            string oldParent = moving.Parents.FirstOrDefault() ?? "";
            ReplaceParent(moving.Commit.Hash, oldParent, moving);
            if (_head == moving.Commit.Hash) { _head = oldParent; }
            bool betweenParentLines = !ReferenceEquals(source, merge) && !ReferenceEquals(neighbor, merge);
            List<string> otherParents = moving.Parents.Skip(1).ToList();
            if (betweenParentLines || direction > 0 && ReferenceEquals(source, moving) || direction < 0 && ReferenceEquals(source, merge))
            {
                // Move the ordinary commit from after the merge onto the selected parent line.
                moving.Parents.Clear();
                moving.Parents.Add(merge.Parents[parent]);
                merge.Parents[parent] = moving.Commit.Hash;
            }
            else
            {
                // Move it from a parent line to immediately after the merge.
                ReplaceParent(merge.Commit.Hash, moving.Commit.Hash, moving);
                moving.Parents.Clear();
                moving.Parents.Add(merge.Commit.Hash);
                if (_head == merge.Commit.Hash) { _head = moving.Commit.Hash; }
            }
            moving.Parents.AddRange(otherParents);
        }
        else
        {
            Group older = direction < 0 ? source : neighbor;
            Group newer = direction < 0 ? neighbor : source;
            string parent = older.Parents.FirstOrDefault() ?? "";
            ReplaceParent(newer.Commit.Hash, older.Commit.Hash, older);
            newer.Parents.Clear();
            if (parent.Length > 0) { newer.Parents.Add(parent); }
            older.Parents.Clear();
            older.Parents.Add(newer.Commit.Hash);
            if (_head == newer.Commit.Hash) { _head = older.Commit.Hash; }
        }
        int sourceIndex = _groups.IndexOf(source);
        int neighborIndex = _groups.IndexOf(neighbor);
        (_groups[sourceIndex], _groups[neighborIndex]) = (_groups[neighborIndex], _groups[sourceIndex]);
        List<Group> ordered = [];
        HashSet<string> visited = [];
        HashSet<string> visiting = [];
        void Visit(Group group)
        {
            if (visited.Contains(group.Commit.Hash)) { return; }
            if (!visiting.Add(group.Commit.Hash)) { throw new InvalidOperationException("The move creates a cycle in the history graph."); }
            foreach (string parent in group.Parents)
            {
                if (Find(parent) is Group ancestor) { Visit(ancestor); }
            }
            visiting.Remove(group.Commit.Hash);
            visited.Add(group.Commit.Hash);
            ordered.Add(group);
        }
        foreach (Group group in _groups) { Visit(group); }
        _groups = ordered;
    }

    public bool CanMoveToParent(string hash, int direction, int parentIndex)
    {
        GitCommit? boundary = MoveBoundary(hash, direction);
        if (boundary is null || boundary.Hash != hash) { return true; }
        string? parent = Find(hash)?.Parents.ElementAtOrDefault(parentIndex - 1);
        return parent is not null && ResolveGroup(parent) is Group group && IsActive(group.Commit.Hash);
    }

    private string BuildGraphPlan(string nativePlan)
    {
        List<string> hashes = [];
        foreach (string line in nativePlan.Split('\n'))
        {
            string[] parts = line.TrimStart().Split([' ', '\t'], 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0].StartsWith('#')) { continue; }
            if (parts[0] is "pick" or "p" && parts.Length >= 2) { hashes.Add(parts[1]); }
            else if (parts[0] is "merge" or "m" && parts.Length >= 3 && parts[1] is "-C" or "-c") { hashes.Add(parts[2]); }
            else if (parts[0] is not ("label" or "l" or "reset" or "t" or "noop"))
            { throw new InvalidOperationException("Unexpected Git todo command."); }
        }
        if (hashes.Count != _original.Count || _original.Any(commit => hashes.Count(hash => commit.Hash.StartsWith(hash, StringComparison.OrdinalIgnoreCase)) != 1))
        { throw new InvalidOperationException("The Git todo range no longer matches the draft."); }

        List<string> plan = ["label onto"];
        HashSet<string> emitted = [];
        HashSet<string> visiting = [];
        string Label(string hash) => "sg-" + hash;
        string Reference(string hash) => Find(hash) is { Deleted: false } ? Label(hash) : hash;
        bool Ancestor(string ancestor, string descendant, HashSet<string> visited)
        {
            if (ancestor == descendant) { return true; }
            if (!visited.Add(descendant) || Find(descendant) is not Group node) { return false; }
            return node.Parents.Select(ResolveParent).Any(parent => Ancestor(ancestor, parent, visited));
        }
        void Emit(Group group)
        {
            if (group.Deleted || emitted.Contains(group.Commit.Hash)) { return; }
            if (!visiting.Add(group.Commit.Hash)) { throw new InvalidOperationException("The history draft contains a cycle."); }
            string[] parents = group.Parents.Select(ResolveParent).Where(hash => hash.Length > 0).Distinct().ToArray();
            if (group.Commit.IsMerge)
            {
                string[] candidates = parents;
                parents = candidates.Where(parent => !candidates.Any(other => other != parent && Ancestor(parent, other, []))).ToArray();
            }
            foreach (string parent in parents)
            {
                if (Find(parent) is Group parentGroup) { Emit(parentGroup); }
            }
            plan.Add(parents.Length == 0 ? "reset [new root]" : "reset " + Reference(parents[0]));
            if (group.Commit.IsMerge && parents.Length > 1)
            {
                plan.Add($"merge {(group.Message == group.Commit.Message ? "-C" : "-c")} {group.Commit.Hash} " + string.Join(' ', parents.Skip(1).Select(Reference)));
            }
            else if (group.Commit.IsMerge)
            {
                // Preserve the merge's message as an ordinary empty commit when its parents collapse.
                plan.Add($"exec git commit --allow-empty -c {group.Commit.Hash}");
            }
            else { plan.Add($"{(group.Message == group.Commit.Message ? "pick" : "reword")} {group.Commit.Hash}"); }
            foreach (GitCommit member in group.Members.Skip(1))
            {
                if (member.IsMerge)
                {
                    int mainline = group.Mainlines.GetValueOrDefault(member.Hash, 1);
                    plan.Add($"exec git cherry-pick --no-commit --mainline {mainline} {member.Hash} && git commit --amend --no-edit --allow-empty");
                }
                else { plan.Add("fixup " + member.Hash); }
            }
            plan.Add("label " + Label(group.Commit.Hash));
            visiting.Remove(group.Commit.Hash);
            emitted.Add(group.Commit.Hash);
        }
        foreach (Group group in _groups) { Emit(group); }
        string head = ResolveParent(_head);
        plan.Add(head.Length == 0 ? "reset onto" : "reset " + Reference(head));
        return string.Join('\n', plan) + "\n";
    }
}
