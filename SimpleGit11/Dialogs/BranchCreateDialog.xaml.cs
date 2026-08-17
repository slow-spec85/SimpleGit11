using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Models;
using SimpleGit11.ViewModels;
using System;

namespace SimpleGit11.Dialogs;

public sealed partial class BranchCreateDialog : ContentDialog
{
    private string? _pendingStartPointSuggestion;

    public BranchCreateDialog(BranchCreateDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Opened += BranchCreateDialog_Opened;
        Closed += BranchCreateDialog_Closed;
    }

    public BranchCreateDialogViewModel ViewModel { get; }

    private async void BranchCreateDialog_Opened(
        ContentDialog sender,
        ContentDialogOpenedEventArgs args)
    {
        await ViewModel.RevisionSelector.LoadSelectedSourceAsync();
    }

    private async void SourceComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        await ViewModel.RevisionSelector.LoadSelectedSourceAsync();
    }

    private void StartPointAutoSuggestBox_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        string? pendingSuggestion = _pendingStartPointSuggestion;
        _pendingStartPointSuggestion = null;
        if (string.Equals(sender.Text, pendingSuggestion, StringComparison.Ordinal))
        {
            return;
        }

        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.RevisionSelector.FilterSuggestions(sender.Text);
        }
    }

    private void StartPointAutoSuggestBox_SuggestionChosen(
        AutoSuggestBox sender,
        AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is GitRevisionSuggestion suggestion)
        {
            _pendingStartPointSuggestion = suggestion.Value;
            ViewModel.RevisionSelector.SelectSuggestion(suggestion);
        }
    }

    private async void BranchCreateDialog_PrimaryButtonClick(
        ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
    {
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        try
        {
            args.Cancel = !await ViewModel.ResolveStartPointAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void BranchCreateDialog_Closed(
        ContentDialog sender,
        ContentDialogClosedEventArgs args)
    {
        ViewModel.Dispose();
    }
}
