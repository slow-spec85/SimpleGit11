using CommunityToolkit.Mvvm.ComponentModel;
using SimpleGit11.Models;
using SimpleGit11.Presentation.Commits;

namespace SimpleGit11.ViewModels;

public sealed class CommitBrowserRowViewItem : ObservableObject
{
    private CommitGraphRow _graph;
    private bool _showGraph;
    private string _accessibleGraphDescription;

    public CommitBrowserRowViewItem(
        GitCommit commit,
        CommitGraphRow graph,
        bool showGraph,
        string accessibleGraphDescription)
    {
        Commit = commit;
        _graph = graph;
        _showGraph = showGraph;
        _accessibleGraphDescription = accessibleGraphDescription;
    }

    public GitCommit Commit { get; }

    public CommitGraphRow Graph
    {
        get => _graph;
        private set => SetProperty(ref _graph, value);
    }

    public bool ShowGraph
    {
        get => _showGraph;
        private set => SetProperty(ref _showGraph, value);
    }

    public string AccessibleGraphDescription
    {
        get => _accessibleGraphDescription;
        private set => SetProperty(ref _accessibleGraphDescription, value);
    }

    public void UpdateGraph(CommitGraphRow graph, bool showGraph, string accessibleGraphDescription)
    {
        Graph = graph;
        ShowGraph = showGraph;
        AccessibleGraphDescription = accessibleGraphDescription;
    }
}
