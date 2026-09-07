using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Dialogs;

public sealed partial class AboutDialog : ContentDialog
{
    public AboutDialog(AboutDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Opened += AboutDialog_Opened;
        Closed += AboutDialog_Closed;
        ViewModel.InstallerLaunched += ViewModel_InstallerLaunched;
    }

    public AboutDialogViewModel ViewModel { get; }

    private async void AboutDialog_Opened(
        ContentDialog sender,
        ContentDialogOpenedEventArgs args)
    {
        await ViewModel.LoadAsync();
    }

    private void AboutDialog_Closed(
        ContentDialog sender,
        ContentDialogClosedEventArgs args)
    {
        ViewModel.InstallerLaunched -= ViewModel_InstallerLaunched;
        ViewModel.Dispose();
    }

    private static void ViewModel_InstallerLaunched(object? sender, System.EventArgs args)
    {
        Application.Current.Exit();
    }
}
