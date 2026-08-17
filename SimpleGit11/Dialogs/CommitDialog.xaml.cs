using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using SimpleGit11.Models;
using SimpleGit11.ViewModels;
using Windows.System;

namespace SimpleGit11.Dialogs;

public sealed partial class CommitDialog : ContentDialog
{
    private int _mentionStart = -1;
    private int _mentionEnd = -1;
    private bool _suppressSuggestionUpdate;

    public CommitDialog(CommitDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public CommitDialogViewModel ViewModel { get; }

    private void CommitMessageTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateFileSuggestions();
    }

    private void CommitMessageTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateFileSuggestions();
    }

    private void CommitMessageTextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        HandleFileSuggestionsKeyDown(e);
    }

    private void FileSuggestions_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        HandleFileSuggestionsKeyDown(e);
    }

    private void CommitFileSuggestionsListView_ItemClick(
        object sender,
        ItemClickEventArgs e)
    {
        if (e.ClickedItem is GitChangedFile file)
        {
            InsertFileName(file);
        }
    }

    private void UpdateFileSuggestions()
    {
        if (_suppressSuggestionUpdate)
        {
            return;
        }

        if (!TryGetActiveMention(out int mentionStart, out int mentionEnd, out string query))
        {
            HideFileSuggestions();
            return;
        }

        _mentionStart = mentionStart;
        _mentionEnd = mentionEnd;
        ViewModel.FilterFileSuggestions(query);

        if (ViewModel.FileSuggestions.Count == 0)
        {
            HideFileSuggestions();
            return;
        }

        CommitFileSuggestionsListView.SelectedIndex = 0;
        if (!FileSuggestionsFlyout.IsOpen)
        {
            FlyoutShowOptions options = new()
            {
                Placement = FlyoutPlacementMode.Bottom,
                ShowMode = FlyoutShowMode.Transient
            };
            FileSuggestionsFlyout.ShowAt(CommitMessageTextBox, options);
        }
    }

    private bool TryGetActiveMention(
        out int mentionStart,
        out int mentionEnd,
        out string query)
    {
        mentionStart = -1;
        mentionEnd = -1;
        query = "";

        string text = CommitMessageTextBox.Text;
        int caretPosition = CommitMessageTextBox.SelectionStart;
        if (caretPosition <= 0 || caretPosition > text.Length)
        {
            return false;
        }

        int triggerIndex = text.LastIndexOf('@', caretPosition - 1, caretPosition);
        if (triggerIndex < 0 || IsPartOfWord(text, triggerIndex))
        {
            return false;
        }

        string candidateQuery = text[(triggerIndex + 1)..caretPosition];
        if (candidateQuery.IndexOfAny([' ', '\t', '\r', '\n']) >= 0)
        {
            return false;
        }

        mentionStart = triggerIndex;
        mentionEnd = caretPosition;
        query = candidateQuery;
        return true;
    }

    private static bool IsPartOfWord(string text, int triggerIndex)
    {
        if (triggerIndex == 0)
        {
            return false;
        }

        char previousCharacter = text[triggerIndex - 1];
        return char.IsLetterOrDigit(previousCharacter)
            || previousCharacter is '_' or '.' or '-';
    }

    private void HandleFileSuggestionsKeyDown(KeyRoutedEventArgs e)
    {
        if (!FileSuggestionsFlyout.IsOpen)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Down:
                MoveFileSuggestionSelection(1);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                MoveFileSuggestionSelection(-1);
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                if (CommitFileSuggestionsListView.SelectedItem is GitChangedFile file)
                {
                    InsertFileName(file);
                    e.Handled = true;
                }
                break;
            case VirtualKey.Escape:
                HideFileSuggestions();
                CommitMessageTextBox.Focus(FocusState.Programmatic);
                e.Handled = true;
                break;
        }
    }

    private void MoveFileSuggestionSelection(int offset)
    {
        int count = ViewModel.FileSuggestions.Count;
        if (count == 0)
        {
            return;
        }

        int currentIndex = CommitFileSuggestionsListView.SelectedIndex;
        int nextIndex = currentIndex < 0
            ? 0
            : (currentIndex + offset + count) % count;

        CommitFileSuggestionsListView.SelectedIndex = nextIndex;
        CommitFileSuggestionsListView.ScrollIntoView(
            CommitFileSuggestionsListView.SelectedItem);
    }

    private void InsertFileName(GitChangedFile file)
    {
        string text = CommitMessageTextBox.Text;
        if (_mentionStart < 0
            || _mentionEnd < _mentionStart
            || _mentionEnd > text.Length)
        {
            HideFileSuggestions();
            return;
        }

        string updatedText = text[.._mentionStart]
            + file.FileName
            + text[_mentionEnd..];
        int caretPosition = _mentionStart + file.FileName.Length;

        _suppressSuggestionUpdate = true;
        CommitMessageTextBox.Text = updatedText;
        ViewModel.Message = updatedText;
        CommitMessageTextBox.SelectionStart = caretPosition;
        CommitMessageTextBox.SelectionLength = 0;
        _suppressSuggestionUpdate = false;

        HideFileSuggestions();
        CommitMessageTextBox.Focus(FocusState.Programmatic);
    }

    private void HideFileSuggestions()
    {
        if (FileSuggestionsFlyout.IsOpen)
        {
            FileSuggestionsFlyout.Hide();
        }

        _mentionStart = -1;
        _mentionEnd = -1;
    }
}
