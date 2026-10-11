using System;
using System.Linq;
using System.ComponentModel;
using Microsoft.UI.Input;
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
    private bool _restoringSelection;
    private CommitBrowserRowViewItem? _dragAnchor;
    private bool _dragSelecting;

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(CommitBrowserViewModelBase),
        typeof(CommitBrowserView),
        new PropertyMetadata(null, OnViewModelChanged));

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

    public static readonly DependencyProperty CommitListCommandsProperty = DependencyProperty.Register(
        nameof(CommitListCommands), typeof(object), typeof(CommitBrowserView), new PropertyMetadata(null));

    public object? CommitListCommands
    {
        get => GetValue(CommitListCommandsProperty);
        set => SetValue(CommitListCommandsProperty, value);
    }

    public CommitBrowserView()
    {
        InitializeComponent();
        Loaded += (_, _) => RestoreSelectionMode();
        HistoryCommitsListView.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(CommitList_PointerPressed), true);
        HistoryCommitsListView.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(CommitList_PointerMoved), true);
        HistoryCommitsListView.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(CommitList_PointerEnded), true);
        HistoryCommitsListView.PointerCanceled += CommitList_PointerEnded;
        HistoryCommitsListView.PointerCaptureLost += (_, args) =>
        {
            if (ReferenceEquals(args.OriginalSource, HistoryCommitsListView)) { CommitList_PointerEnded(HistoryCommitsListView, args); }
        };
        Unloaded += (_, _) => { _dragAnchor = null; _dragSelecting = false; HistoryCommitsListView.ReleasePointerCaptures(); };
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
        if (_restoringSelection) { return; }
        _restoringSelection = true;
        try
        {
            if (ViewModel is not null)
            {
                ViewModel.SelectedCommitRow = HistoryCommitsListView.SelectedItem as CommitBrowserRowViewItem;
                ViewModel.SetSelectedCommits(HistoryCommitsListView.SelectedItems
                    .OfType<CommitBrowserRowViewItem>().Select(item => item.Commit));
            }
        }
        finally { _restoringSelection = false; }
        if (e.AddedItems.Count > 0 && !_dragSelecting)
        {
            HistoryCommitsListView.ScrollIntoView(e.AddedItems[0]);
        }
    }

    private static void OnViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        CommitBrowserView view = (CommitBrowserView)sender;
        if (args.OldValue is CommitBrowserViewModelBase previous) { previous.PropertyChanged -= view.ViewModel_PropertyChanged; }
        if (args.NewValue is CommitBrowserViewModelBase current) { current.PropertyChanged += view.ViewModel_PropertyChanged; }
        view.RestoreSelectionMode();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_restoringSelection) { return; }
        if (args.PropertyName == nameof(CommitBrowserViewModelBase.CommitSelectionMode)) { RestoreSelectionMode(); }
        else if (args.PropertyName == nameof(CommitBrowserViewModelBase.SelectedCommitRow))
        {
            HistoryCommitsListView.SelectedItem = ViewModel?.SelectedCommitRow;
        }
    }

    private void RestoreSelectionMode()
    {
        if (ViewModel is null) { return; }
        GitCommit[] selected = ViewModel.SelectedCommits.ToArray();
        CommitBrowserRowViewItem? focused = ViewModel.SelectedCommitRow;
        _restoringSelection = true;
        try
        {
            _dragAnchor = null;
            _dragSelecting = false;
            HistoryCommitsListView.ReleasePointerCaptures();
            HistoryCommitsListView.SelectionMode = ViewModel.CommitSelectionMode;
            HistoryCommitsListView.SelectedItem = focused;
            if (HistoryCommitsListView.SelectionMode != ListViewSelectionMode.Single)
            {
                HistoryCommitsListView.SelectedItems.Clear();
                foreach (CommitBrowserRowViewItem row in ViewModel.CommitRows.Where(row => selected.Any(commit => commit.Hash == row.Commit.Hash)))
                { HistoryCommitsListView.SelectedItems.Add(row); }
                if (focused is not null && !HistoryCommitsListView.SelectedItems.Contains(focused))
                { HistoryCommitsListView.SelectedItems.Add(focused); }
            }
        }
        finally { _restoringSelection = false; }
    }

    private void CommitList_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        _dragAnchor = null;
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse
            || !args.GetCurrentPoint(HistoryCommitsListView).Properties.IsLeftButtonPressed
            || HistoryCommitsListView.SelectionMode is not (ListViewSelectionMode.Extended or ListViewSelectionMode.Multiple)) { return; }
        _dragAnchor = FindCommitRowDataContext(args.OriginalSource as DependencyObject);
    }

    private void CommitList_PointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_dragAnchor is null) { return; }
        if (!args.GetCurrentPoint(HistoryCommitsListView).Properties.IsLeftButtonPressed)
        { CommitList_PointerEnded(sender, args); return; }
        CommitBrowserRowViewItem? row = VisualTreeHelper.FindElementsInHostCoordinates(args.GetCurrentPoint(null).Position, HistoryCommitsListView)
            .Select(element => FindCommitRowDataContext(element)).FirstOrDefault(item => item is not null);
        if (row is null || (!_dragSelecting && ReferenceEquals(row, _dragAnchor))) { return; }
        int anchor = HistoryCommitsListView.Items.IndexOf(_dragAnchor);
        int end = HistoryCommitsListView.Items.IndexOf(row);
        if (anchor < 0 || end < 0) { CommitList_PointerEnded(sender, args); return; }
        _dragSelecting = true;
        HistoryCommitsListView.CapturePointer(args.Pointer);
        _restoringSelection = true;
        try
        {
            for (int index = 0; index < HistoryCommitsListView.Items.Count; index++)
            {
                if (HistoryCommitsListView.Items[index] is not CommitBrowserRowViewItem item) { continue; }
                bool include = item.IsSelectable && index >= Math.Min(anchor, end) && index <= Math.Max(anchor, end);
                bool selected = HistoryCommitsListView.SelectedItems.Contains(item);
                if (include && !selected) { HistoryCommitsListView.SelectedItems.Add(item); }
                else if (!include && selected) { HistoryCommitsListView.SelectedItems.Remove(item); }
            }
            if (ViewModel is not null) { ViewModel.SelectedCommitRow = row; }
            ViewModel?.SetSelectedCommits(HistoryCommitsListView.SelectedItems.OfType<CommitBrowserRowViewItem>().Select(item => item.Commit));
        }
        finally { _restoringSelection = false; }
        args.Handled = true;
    }

    private void CommitList_PointerEnded(object sender, PointerRoutedEventArgs args)
    {
        _dragAnchor = null;
        _dragSelecting = false;
        HistoryCommitsListView.ReleasePointerCapture(args.Pointer);
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
            || !row.IsSelectable
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
