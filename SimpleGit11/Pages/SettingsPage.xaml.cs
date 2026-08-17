using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Presentation.Navigation;
using SimpleGit11.ViewModels;
using System.Threading.Tasks;

namespace SimpleGit11.Pages;

public sealed partial class SettingsPage : Page, IPageRefreshTarget
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
    }

    public Task RefreshAsync()
    {
        return ViewModel.RefreshSettingsAsync();
    }
}
