using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Models;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Dialogs;

public sealed partial class RevisionSelectDialog : ContentDialog
{
    public RevisionSelectDialog(GitRevisionSelectorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Opened += async (_, _) => await ViewModel.LoadSelectedSourceAsync();
    }

    public GitRevisionSelectorViewModel ViewModel { get; }

    private async void Source_SelectionChanged(object sender, SelectionChangedEventArgs args)
        => await ViewModel.LoadSelectedSourceAsync();

    private void Revision_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) { ViewModel.FilterSuggestions(sender.Text); }
    }

    private void Revision_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is GitRevisionSuggestion suggestion) { ViewModel.SelectSuggestion(suggestion); }
    }

    private async void Dialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        try { args.Cancel = !await ViewModel.ResolveAsync(); }
        finally { deferral.Complete(); }
    }
}
