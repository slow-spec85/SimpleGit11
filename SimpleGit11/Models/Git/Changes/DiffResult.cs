using System.Collections.Generic;

namespace SimpleGit11.Models;

public sealed class DiffResult
{
    public DiffResult(
        IReadOnlyList<DiffLine> lines,
        bool isBinary,
        bool isEmpty,
        string emptyMessage,
        DiffStat? stat = null)
    {
        Lines = lines;
        IsBinary = isBinary;
        IsEmpty = isEmpty;
        EmptyMessage = emptyMessage;
        Stat = stat ?? DiffStat.FromLines(lines);
    }

    public IReadOnlyList<DiffLine> Lines { get; }

    public DiffStat Stat { get; }

    public bool IsBinary { get; }

    public bool IsEmpty { get; }

    public string EmptyMessage { get; }
}
