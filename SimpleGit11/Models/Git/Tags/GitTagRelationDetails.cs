using System.Collections.Generic;

namespace SimpleGit11.Models;

public sealed record GitTagRelationDetails(
    int CommitsOnlyInCurrent,
    int CommitsOnlyInTag,
    GitCommit? MergeBaseCommit,
    IReadOnlyList<string> ContainingLocalBranches);
