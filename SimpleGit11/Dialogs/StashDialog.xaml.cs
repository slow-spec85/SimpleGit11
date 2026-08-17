using Microsoft.UI.Xaml.Controls;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Dialogs;

public sealed partial class StashDialog : Flyout
{
    public StashDialog()
        : this(App.GetService<ChangesViewModel>())
    {
    }

    public StashDialog(ChangesViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        RootGrid.DataContext = ViewModel;
    }

    public ChangesViewModel ViewModel { get; }
}
