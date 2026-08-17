using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Dialogs;

public sealed partial class TextInputDialog : ContentDialog
{
    public TextInputDialog(TextInputDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Opened += TextInputDialog_Opened;
    }

    public TextInputDialogViewModel ViewModel { get; }

    private void TextInputDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        _ = InputTextBox.DispatcherQueue.TryEnqueue(() =>
        {
            InputTextBox.Focus(FocusState.Programmatic);
            InputTextBox.Select(0, InputTextBox.Text.Length);
        });
    }
}
