using Microsoft.UI.Xaml.Controls;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Dialogs;

public sealed partial class SubmoduleAddDialog : ContentDialog
{
    public SubmoduleAddDialog(SubmoduleAddDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public SubmoduleAddDialogViewModel ViewModel { get; }
}
