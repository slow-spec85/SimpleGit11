using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Models;
using SimpleGit11.ViewModels;
using System;

namespace SimpleGit11.Dialogs;

public sealed partial class ArchiveDialog : ContentDialog
{
    private string? _pendingStartPointSuggestion;

    public ArchiveDialog(ArchiveDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Opened += ArchiveDialog_Opened;
        Closed += ArchiveDialog_Closed;
    }

    public ArchiveDialogViewModel ViewModel { get; }

    private async void ArchiveDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
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
        if (args.SelectedItem is GitRevisionSuggestion startPoint)
        {
            _pendingStartPointSuggestion = startPoint.Value;
            ViewModel.RevisionSelector.SelectSuggestion(startPoint);
        }
    }

    private async void ArchiveDialog_PrimaryButtonClick(
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

    private void ArchiveDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        ViewModel.Dispose();
    }
}
