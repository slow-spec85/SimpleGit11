using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Models;
using SimpleGit11.Presentation.Navigation;
using SimpleGit11.ViewModels;
using System.Threading.Tasks;

namespace SimpleGit11.Pages;

public sealed partial class SynchronizationPage : Page, IPageRefreshTarget, IRemoteSelectionRefreshPage
{
    public SynchronizationPage()
    {
        ViewModel = App.GetService<SynchronizationViewModel>();
        InitializeComponent();
        ViewModel.InitializeDispatcherQueue(DispatcherQueue);
    }

    public SynchronizationViewModel ViewModel { get; }

    public Task RefreshAsync()
    {
        return ViewModel.RefreshSynchronizationLocalAsync();
    }

    public Task RefreshSelectedRemoteAsync()
    {
        return ViewModel.RefreshSynchronizationLocalAsync();
    }

    private void OutgoingBranchCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: BranchSynchronizationViewItem item }
            || ViewModel.SelectedRemote is not GitRemote remote
            || !item.CanViewOutgoingCommits)
        {
            return;
        }

        Frame.Navigate(
            typeof(CommitRangePage),
            new CommitRangeNavigationArgs(CommitRangeDirection.Outgoing, remote, item.Branch));
    }

    private void IncomingBranchCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: BranchSynchronizationViewItem item }
            || ViewModel.SelectedRemote is not GitRemote remote)
        {
            return;
        }

        Frame.Navigate(
            typeof(CommitRangePage),
            new CommitRangeNavigationArgs(CommitRangeDirection.Incoming, remote, item.Branch));
    }

}
