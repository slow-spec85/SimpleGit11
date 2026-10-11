using System;
using System.Collections.Generic;
using System.Linq;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Presentation.Commits;

// Owns the draft, including merged groups and undo; never changes repository data.
public sealed partial class HistoryEditSession
{
    private List<Group> _groups;
    private readonly Stack<(List<Group> Groups, string Head)> _undo = new();
    private readonly IReadOnlyList<GitCommit> _original;

    public HistoryEditSession(IReadOnlyList<GitCommit> newestFirst, IReadOnlySet<string>? editableHashes = null)
    {
        if (newestFirst.Count == 0)
        {
            throw new ArgumentException("A non-empty commit range is required.", nameof(newestFirst));
        }
        _original = newestFirst.ToArray();
        _groups = newestFirst.Reverse().Select(commit => new Group(commit)).ToList();
        _editable = editableHashes ?? newestFirst.Select(commit => commit.Hash).ToHashSet();
        _head = newestFirst[0].Hash;
        _hasGraph = newestFirst.Any(commit => commit.IsMerge);
        if (newestFirst.All(commit => commit.ParentHashes.Count == 0))
        {
            for (int index = 1; index < _groups.Count; index++) { _groups[index].Parents.Add(_groups[index - 1].Commit.Hash); }
        }
    }

    public IReadOnlyList<GitCommit> Original => _original.Where(commit => _editable.Contains(commit.Hash)).ToArray();
    public int Revision { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool HasChanges => _groups.Any(group => group.Deleted || group.Members.Count > 1 || group.Message != group.Commit.Message
        || ParentsChanged(group))
        || !_groups.Select(group => group.Commit.Hash).SequenceEqual(_original.Reverse().Select(commit => commit.Hash));

    public bool IsActive(string hash) => _editable.Contains(hash) && Find(hash) is Group group && !group.Deleted;
    public bool IsDeleted(string hash) => Find(hash)?.Deleted == true;
    public string Message(string hash) => Find(hash)?.Message ?? _original.First(commit => commit.Hash == hash).Message;
    public GitCommit? Previous(string hash)
    {
        if (_hasGraph)
        {
            string? parent = Find(hash)?.Parents.FirstOrDefault();
            return parent is null ? null : ResolveGroup(parent)?.Commit;
        }
        int index = _groups.FindIndex(group => group.Commit.Hash == hash && !group.Deleted);
        return index < 0 ? null : _groups.Take(index).LastOrDefault(group => !group.Deleted && _editable.Contains(group.Commit.Hash))?.Commit;
    }

    public bool CanMove(string hash, int direction)
    {
        int index = _groups.FindIndex(group => group.Commit.Hash == hash && !group.Deleted);
        return index >= 0 && Neighbor(index, direction) >= 0;
    }

    // Direction is visual: -1 moves up (towards newer commits), +1 moves down.
    public void Move(string hash, int direction, int? parentIndex = null)
    {
        int index = _groups.FindIndex(group => group.Commit.Hash == hash && !group.Deleted);
        if (index < 0) { return; }
        int neighbor = Neighbor(index, direction);
        if (neighbor < 0) { return; }
        SaveUndo();
        if (_hasGraph)
        {
            try { MoveGraph(_groups[index], _groups[neighbor], direction, parentIndex); }
            catch (InvalidOperationException) { Undo(); throw; }
        }
        else { (_groups[index], _groups[neighbor]) = (_groups[neighbor], _groups[index]); }
    }

    public void SetDeleted(string hash, bool deleted)
    {
        if (Find(hash) is not Group group || group.Deleted == deleted) { return; }
        SaveUndo();
        group.Deleted = deleted;
    }

    public void SetMessage(string hash, string message)
    {
        if (Find(hash) is not Group group || group.Deleted || group.Message == message) { return; }
        SaveUndo();
        group.Message = message;
    }

    public void Merge(string hash, string? message = null, int? parentIndex = null)
    {
        Group? source = Find(hash);
        GitCommit? previous = MergeTarget(hash, parentIndex);
        if (source is null || previous is null) { return; }
        Group target = Find(previous.Hash)!;
        SaveUndo();
        if (_hasGraph)
        {
            string replacement = source.Parents.ElementAtOrDefault(source.Commit.IsMerge ? (parentIndex ?? 1) - 1 : 0) ?? target.Commit.Hash;
            ReplaceParent(source.Commit.Hash, replacement, source);
            if (_head == source.Commit.Hash) { _head = replacement; }
            if (source.Commit.IsMerge) { target.Mainlines[source.Commit.Hash] = parentIndex ?? 1; }
            foreach (KeyValuePair<string, int> mainline in source.Mainlines) { target.Mainlines[mainline.Key] = mainline.Value; }
        }
        target.Members.AddRange(source.Members);
        target.Message = message ?? target.Message;
        _groups.Remove(source);
    }

    public void Undo()
    {
        if (_undo.TryPop(out (List<Group> Groups, string Head) state)) { _groups = state.Groups; _head = state.Head; Revision++; }
    }

    public IEnumerable<CommitBrowserRowViewItem> CreateRows(ILocalizationService strings)
    {
        string[] activeOrder = _groups.Where(group => !group.Deleted).Select(group => group.Commit.Hash).ToArray();
        string[] originalOrder = _original.Reverse().Select(commit => commit.Hash).Where(activeOrder.Contains).ToArray();
        HashSet<string> movedHashes = activeOrder.Where((hash, index) => originalOrder[index] != hash).ToHashSet();
        foreach (Group changed in _groups.Where(group => group.MoveDirection != 0 && ParentsChanged(group))) { movedHashes.Add(changed.Commit.Hash); }
        int firstContentChange = _groups.FindIndex(group => group.Deleted || group.Members.Count > 1 || movedHashes.Contains(group.Commit.Hash) || ParentsChanged(group));
        for (int index = _groups.Count - 1; index >= 0; index--)
        {
            Group group = _groups[index];
            if (!_editable.Contains(group.Commit.Hash)) { continue; }
            bool moved = movedHashes.Contains(group.Commit.Hash);
            bool affected = firstContentChange >= 0 && index > firstContentChange || ParentsChanged(group) && !moved;
            foreach (GitCommit member in group.Members.AsEnumerable().Reverse())
            {
                bool merged = member.Hash != group.Commit.Hash;
                string indicator = group.Deleted ? "HistoryEditDeleted" : merged ? "HistoryEditMerged"
                    : group.Members.Count > 1 ? "HistoryEditCombined" : moved ? "HistoryEditMoved"
                    : group.Message != member.Message ? "HistoryEditMessageChanged" : affected ? "HistoryEditAffected" : "";
                string tooltip = group.Deleted ? strings.GetString("HistoryEditDeletedHint") : merged
                    ? string.Format(strings.GetString("HistoryEditMergedInto"), group.Commit.ShortHash, Title(group.Message))
                    : indicator.Length == 0 ? "" : strings.GetString(indicator + "Hint");
                yield return new CommitBrowserRowViewItem(member, CommitGraphRow.Empty, false, "")
                {
                    DraftMessage = merged ? member.Message : group.Message,
                    EditIndicator = indicator.Length == 0 ? "" : strings.GetString(indicator),
                    EditTooltip = tooltip,
                    IsDraftDeleted = group.Deleted,
                    EditGlyph = group.Deleted ? "\uE74D" : merged || group.Members.Count > 1 ? "\uE71B"
                        : moved ? group.MoveDirection != 0 ? group.MoveDirection < 0 ? "\uE74A" : "\uE74B"
                            : Array.IndexOf(activeOrder, group.Commit.Hash) > Array.IndexOf(originalOrder, group.Commit.Hash) ? "\uE74A" : "\uE74B"
                        : indicator.Length > 0 ? "\uE70F" : "",
                    IsDraftContentUnavailable = group.Deleted || merged || group.Members.Count > 1 || moved || affected,
                    DraftOpacity = group.Deleted || merged ? 0.5 : 1
                };
            }
        }
    }

    public string BuildPlan(string gitPlan)
    {
        if (_hasGraph) { return BuildGraphPlan(gitPlan); }
        // Git separates todo instructions with LF. Lone CR and Unicode line
        // separators can occur inside a commit subject and are not commands.
        List<string> hashes = [];
        foreach (string line in gitPlan.Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) { continue; }
            string[] parts = trimmed.Split([' ', '\t'], 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] is not ("pick" or "p"))
            {
                throw new InvalidOperationException("Unexpected Git todo command.");
            }
            hashes.Add(parts[1]);
        }
        // Refuse to overwrite a different range, or a plan that dropped commits silently.
        if (hashes.Count != _original.Count || _original.Any(commit => hashes.Count(hash => commit.Hash.StartsWith(hash, StringComparison.OrdinalIgnoreCase)) != 1))
        {
            throw new InvalidOperationException("The Git todo range no longer matches the draft.");
        }
        List<string> lines = [];
        foreach (Group group in _groups)
        {
            for (int index = 0; index < group.Members.Count; index++)
            {
                GitCommit commit = group.Members[index];
                string action = group.Deleted ? "drop" : index > 0 ? "fixup"
                    : group.Message == commit.Message ? "pick" : "reword";
                lines.Add($"{action} {commit.Hash}");
            }
        }
        return string.Join('\n', lines) + "\n";
    }

    public IReadOnlyDictionary<string, string> PreparedMessages => _groups
        .Where(group => !group.Deleted && group.Message != group.Commit.Message)
        .ToDictionary(group => group.Commit.Hash, group => group.Message.ReplaceLineEndings("\n") + "\n");

    private bool ParentsChanged(Group group) => _hasGraph && !group.Parents.SequenceEqual(group.Commit.ParentHashes);

    private Group? Find(string hash) => _groups.FirstOrDefault(group => group.Commit.Hash == hash);
    private int Neighbor(int index, int direction)
    {
        for (int next = index - direction; next >= 0 && next < _groups.Count; next -= direction)
        {
            if (!_groups[next].Deleted && _editable.Contains(_groups[next].Commit.Hash)) { return next; }
        }
        return -1;
    }
    private void SaveUndo()
    {
        _undo.Push((_groups.Select(group => group.Copy()).ToList(), _head));
        Revision++;
    }
    private static string Title(string message) => message.ReplaceLineEndings("\n").Split('\n')[0];

    private sealed class Group(GitCommit commit)
    {
        public GitCommit Commit { get; } = commit;
        public List<GitCommit> Members { get; } = [commit];
        public string Message { get; set; } = commit.Message;
        public int MoveDirection { get; set; }
        public bool Deleted { get; set; }
        public List<string> Parents { get; } = commit.ParentHashes.ToList();
        public Dictionary<string, int> Mainlines { get; } = [];
        public Group Copy()
        {
            Group copy = new(Commit) { Message = Message, Deleted = Deleted, MoveDirection = MoveDirection };
            copy.Parents.Clear();
            copy.Parents.AddRange(Parents);
            foreach (KeyValuePair<string, int> mainline in Mainlines) { copy.Mainlines.Add(mainline.Key, mainline.Value); }
            copy.Members.Clear();
            copy.Members.AddRange(Members);
            return copy;
        }
    }
}
