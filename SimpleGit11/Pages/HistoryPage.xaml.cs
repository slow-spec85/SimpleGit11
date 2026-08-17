using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Models;
using SimpleGit11.Presentation.Navigation;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Pages;

public sealed partial class HistoryPage : Page, IPageRefreshTarget
{
    public HistoryPage()
    {
        ViewModel = App.GetService<HistoryViewModel>();
        InitializeComponent();
    }

    public HistoryViewModel ViewModel { get; }

    public Task RefreshAsync()
    {
        return ViewModel.RefreshHistoryAsync();
    }

    private void CommitBrowser_ShowMergedCommitsRequested(
        object? sender,
        MergeCommitRangeNavigationArgs arguments)
    {
        Frame.Navigate(typeof(CommitRangePage), arguments);
    }
}
