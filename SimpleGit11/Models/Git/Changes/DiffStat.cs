using System.Collections.Generic;
using System.Linq;

namespace SimpleGit11.Models;

public sealed class DiffStat
{
    public static DiffStat Empty { get; } = new(0, 0);

    public DiffStat(int addedLines, int removedLines)
    {
        AddedLines = addedLines;
        RemovedLines = removedLines;
    }

    public int AddedLines { get; }

    public int RemovedLines { get; }

    public string AddedText => $"+{AddedLines}";

    public string RemovedText => $"-{RemovedLines}";

    public static DiffStat FromLines(IReadOnlyList<DiffLine> lines)
    {
        return new DiffStat(
            lines.Count(line => line.Kind == DiffLineKind.Added),
            lines.Count(line => line.Kind == DiffLineKind.Removed));
    }

    public static DiffStat Sum(IEnumerable<DiffStat> stats)
    {
        return new DiffStat(
            stats.Sum(stat => stat.AddedLines),
            stats.Sum(stat => stat.RemovedLines));
    }
}
