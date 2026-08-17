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

    private void HistoryCommitsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel?.SetSelectedCommits(
            HistoryCommitsListView.SelectedItems.OfType<GitCommit>());
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
        GitCommit? commit = FindCommitDataContext(args.OriginalSource as DependencyObject);
        if (commit is null && !isPointerRequest)
        {
            commit = HistoryCommitsListView.SelectedItem as GitCommit;
        }

        if (commit is null
            || HistoryCommitsListView.ContainerFromItem(commit) is not FrameworkElement container)
        {
            return;
        }

        if (HistoryCommitsListView.SelectionMode == ListViewSelectionMode.Single)
        {
            HistoryCommitsListView.SelectedItem = commit;
        }
        else if (!HistoryCommitsListView.SelectedItems.Contains(commit))
        {
            HistoryCommitsListView.SelectedItems.Clear();
            HistoryCommitsListView.SelectedItem = commit;
        }

        args.Handled = true;
        if (args.TryGetPosition(container, out Windows.Foundation.Point position))
        {
            CommitContextFlyout.ShowAt(container, new FlyoutShowOptions { Position = position });
            return;
        }

        CommitContextFlyout.ShowAt(container);
    }

    private static GitCommit? FindCommitDataContext(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: GitCommit commit })
            {
                return commit;
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
        if (HistoryDiffSplitterRow.ActualHeight > 0)
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
