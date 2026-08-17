using Microsoft.UI.Xaml.Controls;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Dialogs;

public sealed partial class TagCreateDialog : ContentDialog
{
    public TagCreateDialog(TagCreateDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public TagCreateDialogViewModel ViewModel { get; }
}
