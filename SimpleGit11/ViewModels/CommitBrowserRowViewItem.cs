using SimpleGit11.Models;
using SimpleGit11.Presentation.Commits;

namespace SimpleGit11.ViewModels;

public sealed record CommitBrowserRowViewItem(
    GitCommit Commit,
    CommitGraphRow Graph,
    bool ShowGraph,
    string AccessibleGraphDescription);
