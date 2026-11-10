using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SimpleGit11.Models;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Pages;

public sealed partial class CommitRangePage : Page
{
    public CommitRangePage()
    {
        ViewModel = App.GetService<CommitRangeViewModel>();
        InitializeComponent();
    }

    public CommitRangeViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is CommitRangeNavigationArgs arguments)
        {
            SetCommitSelectionMode(arguments.CherryPickScope);
            await ViewModel.InitializeAsync(arguments);
            return;
        }

        if (e.Parameter is MergeCommitRangeNavigationArgs mergeArguments)
        {
            SetCommitSelectionMode(CommitRangeCherryPickScope.None);
            await ViewModel.InitializeAsync(mergeArguments);
            return;
        }

        if (e.Parameter is RevisionRangeNavigationArgs revisionArguments)
        {
            SetCommitSelectionMode(revisionArguments.CherryPickScope);
            await ViewModel.InitializeAsync(revisionArguments);
            return;
        }

        if (e.Parameter is RevisionDiffNavigationArgs diffArguments)
        {
            SetCommitSelectionMode(CommitRangeCherryPickScope.None);
            await ViewModel.InitializeAsync(diffArguments);
            return;
        }

        if (e.Parameter is CommitDiffNavigationArgs commitDiffArguments)
        {
            SetCommitSelectionMode(CommitRangeCherryPickScope.None);
            await ViewModel.InitializeAsync(commitDiffArguments);
            return;
        }


        ViewModel.ShowInvalidNavigationError();
    }

    private void SetCommitSelectionMode(CommitRangeCherryPickScope cherryPickScope)
    {
        CommitBrowser.SetMultipleCommitSelectionEnabled(
            cherryPickScope != CommitRangeCherryPickScope.None);
    }

    private void CommitBrowser_ShowMergedCommitsRequested(
        object? sender,
        MergeCommitRangeNavigationArgs arguments)
    {
        Frame.Navigate(typeof(CommitRangePage), arguments);
    }
}
