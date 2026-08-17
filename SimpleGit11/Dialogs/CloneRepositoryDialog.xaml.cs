using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Dialogs;

public sealed partial class CloneRepositoryDialog : Flyout
{
    public CloneRepositoryDialog()
        : this(App.GetService<RepositoryViewModel>())
    {
    }

    public CloneRepositoryDialog(RepositoryViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        RootPanel.DataContext = ViewModel;
        Opened += CloneRepositoryDialog_Opened;
    }

    public RepositoryViewModel ViewModel { get; }

    private void CloneRepositoryDialog_Opened(object? sender, object e)
    {
        _ = CloneRepositoryUrlInput.DispatcherQueue.TryEnqueue(() =>
        {
            CloneRepositoryUrlInput.Focus(FocusState.Programmatic);
            CloneRepositoryUrlInput.Select(CloneRepositoryUrlInput.Text.Length, 0);
        });
    }
}
