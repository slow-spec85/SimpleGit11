using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Presentation.Navigation;
using SimpleGit11.ViewModels;
using System.Threading.Tasks;

namespace SimpleGit11.Pages;

public sealed partial class RepositoryPage : Page, IPageRefreshTarget, IRemoteSelectionRefreshPage
{
    public RepositoryViewModel ViewModel { get; }

    public RepositoryPage()
    {
        ViewModel = App.GetService<RepositoryViewModel>();
        InitializeComponent();
    }

    public Task RefreshAsync()
    {
        return ViewModel.RefreshCurrentRepositoryAsync();
    }

    private void RemoteStatusSettingsCard_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(SynchronizationPage));
    }

    private void RepositoryStateSettingsCard_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(ChangesPage));
    }

    private void LastCommitSettingsCard_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(HistoryPage));
    }

    public Task RefreshSelectedRemoteAsync()
    {
        return ViewModel.RefreshSelectedRemoteAsync();
    }
}
