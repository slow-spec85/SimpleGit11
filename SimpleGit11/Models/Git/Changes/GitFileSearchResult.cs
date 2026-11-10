using System.Collections.Generic;

namespace SimpleGit11.Models;

public sealed record GitFileSearchResult(
    GitCommit? Commit,
    GitChangedFile File,
    IReadOnlyList<GitFileSearchMatch> Matches);

// Full-file matches use a one-based file line. Diff matches use a zero-based patch row.
public sealed record GitFileSearchMatch(int Line, int Column, int Length, bool IsDiffRow = false);
