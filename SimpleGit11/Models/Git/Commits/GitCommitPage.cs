using System.Collections.Generic;

namespace SimpleGit11.Models;

public sealed record GitCommitPage(
    IReadOnlyList<GitCommit> Commits,
    bool HasMore);
