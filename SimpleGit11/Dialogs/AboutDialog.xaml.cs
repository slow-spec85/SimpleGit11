using Microsoft.UI.Xaml.Controls;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Dialogs;

public sealed partial class AboutDialog : ContentDialog
{
    public AboutDialog(AboutDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Opened += AboutDialog_Opened;
    }

    public AboutDialogViewModel ViewModel { get; }

    private async void AboutDialog_Opened(
        ContentDialog sender,
        ContentDialogOpenedEventArgs args)
    {
        await ViewModel.LoadAsync();
    }
}
