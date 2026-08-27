using Microsoft.UI.Xaml.Controls;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Dialogs;

public sealed partial class GitUrlRewriteDialog : ContentDialog
{
    public GitUrlRewriteDialog(GitUrlRewriteDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public GitUrlRewriteDialogViewModel ViewModel { get; }
}
