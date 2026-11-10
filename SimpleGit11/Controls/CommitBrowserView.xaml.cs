using System;
using System.Linq;
using System.ComponentModel;
using System.Numerics;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Hosting;
using SimpleGit11.Models;
using SimpleGit11.ViewModels;
using Windows.Foundation;

namespace SimpleGit11.Controls;

public delegate void ShowMergedCommitsRequestedEventHandler(
    object sender,
    MergeCommitRangeNavigationArgs arguments);

public sealed partial class CommitBrowserView : UserControl
{
    private static readonly GridLength Fill = new(1, GridUnitType.Star);

    private (GridLength Lists, GridLength Right) _widePaneWidths = (new GridLength(400), Fill);
    private (GridLength Lists, GridLength Right) _narrowPaneHeights =
        (new GridLength(2, GridUnitType.Star), new GridLength(3, GridUnitType.Star));
    private (GridLength Commits, GridLength Files) _listHeights = (Fill, Fill);
    private double _commitDetailsHeight = 220;
    private bool _restoringSelection;
    private string? _viewportCommitHash;
    private double _viewportCommitY;
    private double? _viewportOffset;
    private CommitBrowserRowViewItem? _dragAnchor;
    private bool _dragSelecting;
    private CompositionGeometricClip? _commitListOverlayClip;

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(CommitBrowserViewModelBase),
        typeof(CommitBrowserView),
        new PropertyMetadata(null, OnViewModelChanged));

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

    public static readonly DependencyProperty CommitListOverlayProperty = DependencyProperty.Register(
        nameof(CommitListOverlay),
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
        HistoryChangedFilesTree.FileContextMenu = HistoryFileContextMenu;
        Loaded += (_, _) =>
        {
            UpdatePaneLayout();
            RestoreSelectionMode();
            UpdateCommitListOverlayClip();
        };
        HistoryCommitsListView.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(CommitList_PointerPressed), true);
        HistoryCommitsListView.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(CommitList_PointerMoved), true);
        HistoryCommitsListView.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(CommitList_PointerEnded), true);
        HistoryCommitsListView.PointerCanceled += CommitList_PointerEnded;
        HistoryCommitsListView.PointerCaptureLost += (_, args) =>
        {
            if (ReferenceEquals(args.OriginalSource, HistoryCommitsListView)) { CommitList_PointerEnded(HistoryCommitsListView, args); }
        };
        Unloaded += (_, _) =>
        {
            _dragAnchor = null;
            _dragSelecting = false;
            HistoryCommitsListView.ReleasePointerCaptures();
            ElementCompositionPreview.GetElementVisual(HistoryCommitsListView).Clip = null;
            _commitListOverlayClip?.Geometry.Dispose();
            _commitListOverlayClip?.Dispose();
            _commitListOverlayClip = null;
        };
    }

    public event ShowMergedCommitsRequestedEventHandler? ShowMergedCommitsRequested;

    private void CommitListOverlayLayout_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        UpdateCommitListOverlayClip();
    }

    private void UpdateCommitListOverlayClip()
    {
        if (!IsLoaded) { return; }
        Visual listVisual = ElementCompositionPreview.GetElementVisual(HistoryCommitsListView);
        if (CommitListOverlayPresenter.ActualWidth <= 0 || CommitListOverlayPresenter.ActualHeight <= 0
            || HistoryCommitsListView.ActualWidth <= 0 || HistoryCommitsListView.ActualHeight <= 0)
        {
            listVisual.Clip = null;
            return;
        }

        Rect overlayBounds = CommitListOverlayPresenter.TransformToVisual(HistoryCommitsListView).TransformBounds(
            new Rect(0, 0, CommitListOverlayPresenter.ActualWidth, CommitListOverlayPresenter.ActualHeight));
        CornerRadius cornerRadius = CommitListOverlay is Control control ? control.CornerRadius : new CornerRadius(0);
        using CanvasGeometry geometry = CreateCommitListClipGeometry(
            new Size(HistoryCommitsListView.ActualWidth, HistoryCommitsListView.ActualHeight),
            overlayBounds,
            (float)cornerRadius.TopLeft);
        _commitListOverlayClip ??= listVisual.Compositor.CreateGeometricClip(listVisual.Compositor.CreatePathGeometry());
        ((CompositionPathGeometry)_commitListOverlayClip.Geometry).Path = new CompositionPath(geometry);
        listVisual.Clip = _commitListOverlayClip;
    }

    internal static CanvasGeometry CreateCommitListClipGeometry(Size listSize, Rect overlayBounds, float cornerRadius)
    {
        using CanvasGeometry list = CanvasGeometry.CreateRectangle(null, new Rect(0, 0, listSize.Width, listSize.Height));
        using CanvasGeometry overlay = CanvasGeometry.CreateRoundedRectangle(null, overlayBounds, cornerRadius, cornerRadius);
        return list.CombineWith(overlay, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
    }

    public CommitBrowserViewModelBase? ViewModel
    {
        get => (CommitBrowserViewModelBase?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
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

    public object? CommitListOverlay
    {
        get => GetValue(CommitListOverlayProperty);
        set => SetValue(CommitListOverlayProperty, value);
    }

    private void LayoutStates_CurrentStateChanged(object sender, VisualStateChangedEventArgs e)
    {
        UpdatePaneLayout();
    }

    private void UpdatePaneLayout()
    {
        bool showCommits = ViewModel is { IsCommitHistoryAvailable: true, IsCommitListVisible: true };
        bool showFiles = ViewModel?.IsFileListVisible != false;
        bool showDiff = ViewModel?.IsDiffVisible != false;
        bool showLists = showCommits || showFiles;
        bool showRight = showDiff || ViewModel?.CommitDetailsBlockVisibility == Visibility.Visible;
        bool splitLists = showCommits && showFiles;
        bool narrow = LayoutStates.CurrentState?.Name == "NarrowLayout";
        bool splitPanes = showLists && showRight;
        bool splitWide = !narrow && splitPanes;
        bool splitNarrow = narrow && splitPanes;
        GridLength zero = new(0);

        CommitListGrid.Visibility = showCommits ? Visibility.Visible : Visibility.Collapsed;
        ChangedFilesGrid.Visibility = showFiles ? Visibility.Visible : Visibility.Collapsed;
        CommitListSplitter.Visibility = splitLists ? Visibility.Visible : Visibility.Collapsed;
        CommitsListsRow.Height = showCommits ? (splitLists ? _listHeights.Commits : Fill) : zero;
        FilesListsRow.Height = showFiles ? (splitLists ? _listHeights.Files : Fill) : zero;
        CommitListSplitterRow.Height = new GridLength(splitLists ? 8 : 0);

        HistoryListsPane.Visibility = showLists ? Visibility.Visible : Visibility.Collapsed;
        HistoryRightPane.Visibility = showRight ? Visibility.Visible : Visibility.Collapsed;
        HistoryDiffPane.Visibility = showDiff ? Visibility.Visible : Visibility.Collapsed;
        HistoryDiffSplitter.Visibility = splitPanes ? Visibility.Visible : Visibility.Collapsed;

        HistoryListsColumn.Width = splitWide ? _widePaneWidths.Lists : (narrow || showLists ? Fill : zero);
        HistoryDiffSplitterColumn.Width = new GridLength(splitWide ? 8 : 0);
        HistoryDiffColumn.MinWidth = splitWide ? 360 : 0;
        HistoryDiffColumn.Width = splitWide ? _widePaneWidths.Right : (!narrow && showRight ? Fill : zero);

        HistoryListsRow.Height = splitNarrow ? _narrowPaneHeights.Lists : (!narrow || showLists ? Fill : zero);
        HistoryDiffSplitterRow.Height = new GridLength(splitNarrow ? 8 : 0);
        HistoryDiffRow.Height = splitNarrow ? _narrowPaneHeights.Right : (narrow && showRight ? Fill : zero);

        UpdateCommitDetailsRows();
    }

    public void SetMultipleCommitSelectionEnabled(bool isEnabled)
    {
        HistoryCommitsListView.SelectionMode = isEnabled
            ? ListViewSelectionMode.Extended
            : ListViewSelectionMode.Single;
    }

    private void ChangedFiles_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        GitChangedFile? file = ChangedFilesTree.GetContextFile(args);
        if (file is null && !args.TryGetPosition(HistoryChangedFilesListView, out _))
        {
            file = HistoryChangedFilesListView.SelectedItem as GitChangedFile;
        }
        if (file is not null)
        {
            ChangedFilesTree.ShowFileContextMenu(HistoryFileContextMenu, file, HistoryChangedFilesListView, args);
        }
        else { args.Handled = true; }
    }

    private void ChangedFiles_ContextCanceled(UIElement sender, RoutedEventArgs args)
    {
        HistoryFileContextMenu.Hide();
    }

    private void ChangedFilesTree_FileSelectionChanged(object? sender, EventArgs args)
    {
        if (ViewModel is not null) { ViewModel.SelectedChangedFile = HistoryChangedFilesTree.SelectedFile; }
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

    private void HistoryRightPane_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCommitDetailsRows();
    }

    private void UpdateCommitDetailsRows()
    {
        bool showDetails = ViewModel?.CommitDetailsBlockVisibility == Visibility.Visible;
        bool showDiff = ViewModel?.IsDiffVisible != false;
        bool splitDetails = showDetails && showDiff;
        CommitDetailsSplitter.Visibility = splitDetails ? Visibility.Visible : Visibility.Collapsed;
        CommitDetailsSplitterRow.Height = new GridLength(splitDetails ? 20 : 0);
        DiffContentRow.Height = showDiff ? Fill : new GridLength(0);
        if (!splitDetails)
        {
            CommitDetailsRow.Height = showDetails ? Fill : new GridLength(0);
            return;
        }

        double availableHeight = HistoryRightPane.ActualHeight;
        double detailsHeight = availableHeight > 0
            ? Math.Min(_commitDetailsHeight, Math.Max(0, availableHeight - 20 - 120))
            : _commitDetailsHeight;
        CommitDetailsRow.Height = new GridLength(detailsHeight);
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
        if (_restoringSelection || !IsLoaded || ViewModel?.IsUpdatingCommitRows == true) { return; }
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
        view.UpdatePaneLayout();
        view.RestoreSelectionMode();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(CommitBrowserViewModelBase.IsCommitHistoryAvailable)
            or nameof(CommitBrowserViewModelBase.IsCommitListVisible)
            or nameof(CommitBrowserViewModelBase.IsFileListVisible)
            or nameof(CommitBrowserViewModelBase.IsDiffVisible)
            or nameof(CommitBrowserViewModelBase.CommitDetailsBlockVisibility))
        {
            UpdatePaneLayout();
            return;
        }

        if (_restoringSelection || !IsLoaded) { return; }
        if (args.PropertyName == nameof(CommitBrowserViewModelBase.IsUpdatingCommitRows))
        {
            if (ViewModel?.IsUpdatingCommitRows == true) { CaptureCommitViewport(); return; }
            RestoreSelectionMode();
            RestoreCommitViewport();
            return;
        }
        if (ViewModel?.IsUpdatingCommitRows == true) { return; }
        if (args.PropertyName == nameof(CommitBrowserViewModelBase.CommitSelectionMode)) { RestoreSelectionMode(); }
        else if (args.PropertyName is nameof(CommitBrowserViewModelBase.SelectedCommitRow)
            or nameof(CommitBrowserViewModelBase.IsUpdatingCommitRows))
        {
            RestoreSelectionMode();
            if (ViewModel?.DiffSearch is { SearchHistory: true, IsFiltering: true }
                && ViewModel.SelectedCommitRow is { } row)
            {
                HistoryCommitsListView.ScrollIntoView(row);
            }
        }
        else if (args.PropertyName == nameof(CommitBrowserViewModelBase.SelectedChangedFile)
            && ViewModel?.DiffSearch.IsFiltering == true && !ViewModel.FileTree.ShowFolders
            && ViewModel.SelectedChangedFile is { } file)
        {
            HistoryChangedFilesListView.ScrollIntoView(file);
        }
    }

    private void CaptureCommitViewport()
    {
        _viewportCommitHash = null;
        _viewportOffset = FindCommitScrollViewer(HistoryCommitsListView)?.VerticalOffset;
        if (HistoryCommitsListView.ItemsPanelRoot is ItemsStackPanel panel
            && panel.FirstVisibleIndex >= 0
            && panel.FirstVisibleIndex < HistoryCommitsListView.Items.Count
            && HistoryCommitsListView.Items[panel.FirstVisibleIndex] is CommitBrowserRowViewItem row
            && HistoryCommitsListView.ContainerFromIndex(panel.FirstVisibleIndex) is FrameworkElement container)
        {
            _viewportCommitHash = row.Commit.Hash;
            _viewportCommitY = container.TransformToVisual(HistoryCommitsListView).TransformPoint(new Point()).Y;
        }
    }

    private void RestoreCommitViewport()
    {
        string? hash = _viewportCommitHash;
        double? offset = _viewportOffset;
        double y = _viewportCommitY;
        _viewportOffset = null;
        if (offset is null) { return; }
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsLoaded || ViewModel?.IsUpdatingCommitRows == true) { return; }
            ScrollViewer? viewer = FindCommitScrollViewer(HistoryCommitsListView);
            if (viewer is null) { return; }
            CommitBrowserRowViewItem? row = ViewModel?.CommitRows.FirstOrDefault(item => item.Commit.Hash == hash);
            if (row is not null && HistoryCommitsListView.ContainerFromItem(row) is FrameworkElement container)
            {
                double currentY = container.TransformToVisual(HistoryCommitsListView).TransformPoint(new Point()).Y;
                viewer.ChangeView(null, viewer.VerticalOffset + currentY - y, null, true);
            }
            else { viewer.ChangeView(null, offset, null, true); }
        });
    }

    private static ScrollViewer? FindCommitScrollViewer(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is ScrollViewer viewer) { return viewer; }
            if (FindCommitScrollViewer(child) is ScrollViewer nested) { return nested; }
        }
        return null;
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
                CommitBrowserRowViewItem[] rows = ViewModel.CommitRows.Where(row => selected.Any(commit => commit.Hash == row.Commit.Hash)).ToArray();
                foreach (object item in HistoryCommitsListView.SelectedItems.ToArray())
                {
                    if (!rows.Contains(item) && !ReferenceEquals(item, focused)) { HistoryCommitsListView.SelectedItems.Remove(item); }
                }
                foreach (CommitBrowserRowViewItem row in rows)
                {
                    if (!HistoryCommitsListView.SelectedItems.Contains(row)) { HistoryCommitsListView.SelectedItems.Add(row); }
                }
                if (focused is not null && !HistoryCommitsListView.SelectedItems.Contains(focused))
                { HistoryCommitsListView.SelectedItems.Add(focused); }
            }
        }
        finally { _restoringSelection = false; }
    }

    private void CommitList_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        _dragAnchor = null;
        for (DependencyObject? element = args.OriginalSource as DependencyObject;
            element is not null && !ReferenceEquals(element, HistoryCommitsListView);
            element = VisualTreeHelper.GetParent(element))
        {
            if (element is Button { DataContext: GitCommitReference }) { return; }
        }
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
        _narrowPaneHeights = (new GridLength(listsHeight, GridUnitType.Star),
            new GridLength(availableHeight - listsHeight, GridUnitType.Star));
        HistoryListsRow.Height = _narrowPaneHeights.Lists;
        HistoryDiffRow.Height = _narrowPaneHeights.Right;
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
        _widePaneWidths = (new GridLength(listsWidth, GridUnitType.Star),
            new GridLength(availableWidth - listsWidth, GridUnitType.Star));
        HistoryListsColumn.Width = _widePaneWidths.Lists;
        HistoryDiffColumn.Width = _widePaneWidths.Right;
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
        _listHeights = (new GridLength(commitsListHeight, GridUnitType.Star),
            new GridLength(availableHeight - commitsListHeight, GridUnitType.Star));
        CommitsListsRow.Height = _listHeights.Commits;
        FilesListsRow.Height = _listHeights.Files;
    }
}
