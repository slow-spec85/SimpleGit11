using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SimpleGit11.Models;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Controls;

public delegate void ShowMergedCommitsRequestedEventHandler(
    object sender,
    MergeCommitRangeNavigationArgs arguments);

public sealed partial class CommitBrowserView : UserControl
{
    private double _commitDetailsHeight = 220;

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(CommitBrowserViewModelBase),
        typeof(CommitBrowserView),
        new PropertyMetadata(null));

    public static readonly DependencyProperty IsCommitListVisibleProperty = DependencyProperty.Register(
        nameof(IsCommitListVisible),
        typeof(bool),
        typeof(CommitBrowserView),
        new PropertyMetadata(true));

    public static readonly DependencyProperty CommitContextFlyoutProperty = DependencyProperty.Register(
        nameof(CommitContextFlyout),
        typeof(MenuFlyout),
        typeof(CommitBrowserView),
        new PropertyMetadata(null));

    public static readonly DependencyProperty IsCommitContextFlyoutEnabledProperty = DependencyProperty.Register(
        nameof(IsCommitContextFlyoutEnabled),
        typeof(bool),
        typeof(CommitBrowserView),
        new PropertyMetadata(true));

    public static readonly DependencyProperty CommitListFooterProperty = DependencyProperty.Register(
        nameof(CommitListFooter),
        typeof(object),
        typeof(CommitBrowserView),
        new PropertyMetadata(null));

    public CommitBrowserView()
    {
        InitializeComponent();
    }

    public event ShowMergedCommitsRequestedEventHandler? ShowMergedCommitsRequested;

    public CommitBrowserViewModelBase? ViewModel
    {
        get => (CommitBrowserViewModelBase?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public bool IsCommitListVisible
    {
        get => (bool)GetValue(IsCommitListVisibleProperty);
        set => SetValue(IsCommitListVisibleProperty, value);
    }

    public MenuFlyout? CommitContextFlyout
    {
        get => (MenuFlyout?)GetValue(CommitContextFlyoutProperty);
        set => SetValue(CommitContextFlyoutProperty, value);
    }

    public bool IsCommitContextFlyoutEnabled
    {
        get => (bool)GetValue(IsCommitContextFlyoutEnabledProperty);
        set => SetValue(IsCommitContextFlyoutEnabledProperty, value);
    }

    public object? CommitListFooter
    {
        get => GetValue(CommitListFooterProperty);
        set => SetValue(CommitListFooterProperty, value);
    }

    public void SetCommitListVisible(bool isVisible)
    {
        IsCommitListVisible = isVisible;
        CommitListGrid.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        CommitsListsRow.Height = isVisible
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        FilesListsRow.Height = new GridLength(1, GridUnitType.Star);
        CommitListSplitterRow.Height = isVisible
            ? new GridLength(12)
            : new GridLength(0);
    }

    public void SetMultipleCommitSelectionEnabled(bool isEnabled)
    {
        HistoryCommitsListView.SelectionMode = isEnabled
            ? ListViewSelectionMode.Extended
            : ListViewSelectionMode.Single;
    }

    private void CommitFilterToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button)
        {
            button.IsChecked = ViewModel?.IsCommitFilterApplied == true;
            FlyoutBase.ShowAttachedFlyout(button);
        }
    }

    private void TimeFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            FlyoutBase.ShowAttachedFlyout(element);
        }
    }

    private void CommitDetailsToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button)
        {
            UpdateCommitDetailsRows(button.IsChecked == true);
        }
    }

    private void HistoryRightPane_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (CommitDetailsToggleButton.IsChecked == true)
        {
            UpdateCommitDetailsRows(true);
        }
    }

    private void UpdateCommitDetailsRows(bool visible)
    {
        CommitDetailsSplitterRow.Height = new GridLength(visible ? 20 : 0);
        double availableHeight = HistoryRightPane.ActualHeight;
        double detailsHeight = availableHeight > 0
            ? Math.Min(_commitDetailsHeight, Math.Max(0, availableHeight - 20 - 120))
            : _commitDetailsHeight;
        CommitDetailsRow.Height = new GridLength(visible ? detailsHeight : 0);
    }

    private void CommitDetailsSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double availableHeight = CommitDetailsRow.ActualHeight + HistoryDiffPane.ActualHeight;
        if (availableHeight <= 0)
        {
            return;
        }

        double maximumDetailsHeight = Math.Max(0, availableHeight - 120);
        double minimumDetailsHeight = Math.Min(120, maximumDetailsHeight);
        _commitDetailsHeight = Math.Clamp(
            CommitDetailsRow.ActualHeight + e.VerticalChange,
            minimumDetailsHeight,
            maximumDetailsHeight);
        CommitDetailsRow.Height = new GridLength(_commitDetailsHeight);
    }

    private void CommitMessageContextFlyout_Opening(object sender, object args)
    {
        bool canEdit = ViewModel?.EditCommitMessageCommand.CanExecute(null) == true;
        EditCommitMessageContextButton.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;
        CommitMessageEditSeparator.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;
        CopyCommitMessageButton.IsEnabled = CommitMessageTextBox.Text.Length > 0;
    }

    private void CopyCommitMessageButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommitMessageTextBox.SelectionLength > 0)
        {
            CommitMessageTextBox.CopySelectionToClipboard();
        }
        else
        {
            ViewModel?.CopyTextCommand.Execute(CommitMessageTextBox.Text);
        }
    }

    private void HistoryCommitsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel?.SetSelectedCommits(
            HistoryCommitsListView.SelectedItems
                .OfType<CommitBrowserRowViewItem>()
                .Select(item => item.Commit));
        if (e.AddedItems.Count > 0)
        {
            HistoryCommitsListView.ScrollIntoView(e.AddedItems[0]);
        }
    }

    private void HistoryCommitsListView_ContextRequested(
        UIElement sender,
        ContextRequestedEventArgs args)
    {
        if (!IsCommitContextFlyoutEnabled || CommitContextFlyout is null)
        {
            return;
        }

        bool isPointerRequest = args.TryGetPosition(HistoryCommitsListView, out _);
        CommitBrowserRowViewItem? row = FindCommitRowDataContext(args.OriginalSource as DependencyObject);
        if (row is null && !isPointerRequest)
        {
            row = HistoryCommitsListView.SelectedItem as CommitBrowserRowViewItem;
        }

        if (row is null
            || HistoryCommitsListView.ContainerFromItem(row) is not FrameworkElement container)
        {
            return;
        }

        if (HistoryCommitsListView.SelectionMode == ListViewSelectionMode.Single)
        {
            HistoryCommitsListView.SelectedItem = row;
        }
        else if (!HistoryCommitsListView.SelectedItems.Contains(row))
        {
            HistoryCommitsListView.SelectedItems.Clear();
            HistoryCommitsListView.SelectedItem = row;
        }

        args.Handled = true;
        if (args.TryGetPosition(container, out Windows.Foundation.Point position))
        {
            CommitContextFlyout.ShowAt(container, new FlyoutShowOptions { Position = position });
            return;
        }

        CommitContextFlyout.ShowAt(container);
    }

    private static CommitBrowserRowViewItem? FindCommitRowDataContext(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: CommitBrowserRowViewItem row })
            {
                return row;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void ShowMergedCommitsMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CommitParentViewItem parent }
            || !parent.IsMergedHistory
            || ViewModel?.SelectedCommit is not GitCommit { IsMerge: true } commit
            || commit.ParentHashes.Count < 2)
        {
            return;
        }

        ShowMergedCommitsRequested?.Invoke(
            this,
            new MergeCommitRangeNavigationArgs(
                commit.ShortHash,
                commit.ParentHashes[0],
                parent.Hash));
    }

    private void DiffSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (LayoutStates.CurrentState?.Name == "NarrowLayout")
        {
            ResizeNarrowDiffPanels(e.VerticalChange);
            return;
        }

        ResizeWideDiffPanels(e.HorizontalChange);
    }

    private void ResizeNarrowDiffPanels(double verticalChange)
    {
        double requestedHeight = HistoryListsRow.ActualHeight + verticalChange;
        double availableHeight = HistoryListsRow.ActualHeight + HistoryDiffRow.ActualHeight;

        if (availableHeight <= 0)
        {
            return;
        }

        double listsHeight = Math.Clamp(requestedHeight, 0, availableHeight);
        HistoryListsRow.Height = new GridLength(listsHeight, GridUnitType.Star);
        HistoryDiffRow.Height = new GridLength(availableHeight - listsHeight, GridUnitType.Star);
    }

    private void ResizeWideDiffPanels(double horizontalChange)
    {
        double requestedWidth = HistoryListsColumn.ActualWidth + horizontalChange;
        double availableWidth = HistoryListsColumn.ActualWidth + HistoryDiffColumn.ActualWidth;
        double maxListsWidth = Math.Max(0, availableWidth - HistoryDiffColumn.MinWidth);

        if (availableWidth <= 0)
        {
            return;
        }

        double listsWidth = Math.Clamp(requestedWidth, 0, maxListsWidth);
        HistoryListsColumn.Width = new GridLength(listsWidth, GridUnitType.Star);
        HistoryDiffColumn.Width = new GridLength(availableWidth - listsWidth, GridUnitType.Star);
    }

    private void FilesSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double requestedHeight = CommitsListsRow.ActualHeight + e.VerticalChange;
        double availableHeight = CommitsListsRow.ActualHeight + FilesListsRow.ActualHeight;

        if (availableHeight <= 0)
        {
            return;
        }

        double commitsListHeight = Math.Clamp(requestedHeight, 0, availableHeight);
        CommitsListsRow.Height = new GridLength(commitsListHeight, GridUnitType.Star);
        FilesListsRow.Height = new GridLength(availableHeight - commitsListHeight, GridUnitType.Star);
    }
}
