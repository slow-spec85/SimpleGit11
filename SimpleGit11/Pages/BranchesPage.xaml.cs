using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SimpleGit11.Presentation.Theming;
using SimpleGit11.Presentation.Navigation;
using SimpleGit11.Services;
using SimpleGit11.ViewModels;
using SimpleGit11.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SimpleGit11.Pages;

public sealed partial class BranchesPage : Page, IPageRefreshTarget, IRemoteSelectionRefreshPage
{
    public BranchesViewModel ViewModel { get; }
    private GitCommitReference? _requestedReference;
    private ReferenceTreeItemViewModel? _referenceToReveal;
    private long _navigationVersion;
    private bool _synchronizingReferenceSelection;
    private bool _synchronizingBranchScope;

    public BranchesPage()
    {
        ViewModel = App.GetService<BranchesViewModel>();
        InitializeComponent();
        SynchronizeBranchScope();
        UpdateHeaderReferenceButtons();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateHeaderReferenceButtons();
        SynchronizeBranchScope();
        QueueReferenceSelection();
        QueueReferenceReveal();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _referenceToReveal = null;
        _requestedReference = null;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _navigationVersion++;
        _requestedReference = e.Parameter as GitCommitReference;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _navigationVersion++;
        _requestedReference = null;
        _referenceToReveal = null;
        base.OnNavigatedFrom(e);
    }

    public void RequestReferenceSelection(GitCommitReference reference)
    {
        _requestedReference = reference;
    }

    public async Task RefreshAsync()
    {
        long navigationVersion = _navigationVersion;
        await ViewModel.RefreshBranchesLocalAsync();
        if (navigationVersion != _navigationVersion) { return; }
        if (_requestedReference is not GitCommitReference reference) { return; }
        _requestedReference = null;
        await ViewModel.SelectReferenceAsync(reference);
        if (navigationVersion != _navigationVersion) { return; }
        _referenceToReveal = reference.IsTag ? ViewModel.SelectedTagTreeItem : ViewModel.SelectedBranchTreeItem;
        QueueReferenceSelection();
    }

    private void QueueReferenceReveal()
    {
        if (_referenceToReveal is not ReferenceTreeItemViewModel reference) { return; }
        long navigationVersion = _navigationVersion;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (IsLoaded && navigationVersion == _navigationVersion
                && ReferenceEquals(reference, _referenceToReveal))
            {
                RevealRequestedReference();
            }
        });
    }

    private void QueueReferenceSelection()
    {
        long navigationVersion = _navigationVersion;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!IsLoaded || navigationVersion != _navigationVersion) { return; }
            SynchronizeReferenceSelection(BranchesTreeView, ViewModel.SelectedBranchTreeItem);
            SynchronizeReferenceSelection(TagsTreeView, ViewModel.SelectedTagTreeItem);
            QueueReferenceReveal();
        });
    }

    private void SynchronizeReferenceSelection(TreeView tree, ReferenceTreeItemViewModel? selected)
    {
        _synchronizingReferenceSelection = true;
        try
        {
            if (selected is null)
            {
                tree.SelectedNode = null;
                return;
            }

            Stack<ReferenceTreeItemViewModel> path = new();
            for (ReferenceTreeItemViewModel? item = selected; item is not null; item = item.Parent)
            {
                path.Push(item);
            }

            IList<TreeViewNode> nodes = tree.RootNodes;
            TreeViewNode? node = null;
            while (path.TryPop(out ReferenceTreeItemViewModel? item))
            {
                node = nodes.FirstOrDefault(candidate => ReferenceEquals(candidate.Content, item));
                // Child nodes are supplied by the template after each ancestor is loaded.
                // A matching container's Loaded event will retry the selection.
                if (node is null) { return; }
                if (path.Count > 0) { node.IsExpanded = true; }
                nodes = node.Children;
            }

            if (!ReferenceEquals(tree.SelectedNode, node)) { tree.SelectedNode = node; }
        }
        finally { _synchronizingReferenceSelection = false; }
    }

    private void SynchronizeBranchScope()
    {
        _synchronizingBranchScope = true;
        try
        {
            BranchScopeSelectorBar.SelectedItem = ViewModel.BranchScope == BranchListScope.Remote
                ? RemoteBranchScopeSelectorBarItem : LocalBranchScopeSelectorBarItem;
        }
        finally { _synchronizingBranchScope = false; }
    }

    private void RevealRequestedReference()
    {
        if (_referenceToReveal is null) { return; }
        TreeView tree = _referenceToReveal.Tag is null ? BranchesTreeView : TagsTreeView;
        ReferenceTreeItemViewModel? selected = _referenceToReveal.Tag is null
            ? ViewModel.SelectedBranchTreeItem : ViewModel.SelectedTagTreeItem;
        if (!ReferenceEquals(selected, _referenceToReveal))
        {
            _referenceToReveal = null;
            return;
        }
        if (!ReferenceEquals(tree.SelectedItem, _referenceToReveal))
        {
            return;
        }
        if (tree.ContainerFromItem(_referenceToReveal) is FrameworkElement container)
        {
            _referenceToReveal = null;
            container.StartBringIntoView();
        }
        else if (FindTreeList(tree) is ListView list)
        {
            object? item = list.Items.FirstOrDefault(item => ReferenceEquals(item, _referenceToReveal)
                || item is TreeViewNode node && ReferenceEquals(node.Content, _referenceToReveal));
            if (item is not null) { list.ScrollIntoView(item); }
        }

        static ListView? FindTreeList(DependencyObject parent)
        {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                if (child is ListView list) { return list; }
                if (FindTreeList(child) is ListView nestedList) { return nestedList; }
            }
            return null;
        }
    }

    public Task RefreshSelectedRemoteAsync()
    {
        return ViewModel.RefreshSelectedRemoteAsync();
    }

    private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.SelectedBranchTreeItem))
        {
            if (ViewModel.SelectedBranchTreeItem is null) { SynchronizeReferenceSelection(BranchesTreeView, null); }
            else { QueueReferenceSelection(); }
        }
        else if (e.PropertyName == nameof(ViewModel.SelectedTagTreeItem))
        {
            if (ViewModel.SelectedTagTreeItem is null) { SynchronizeReferenceSelection(TagsTreeView, null); }
            else { QueueReferenceSelection(); }
        }
        else if (e.PropertyName == nameof(ViewModel.ReferenceKind))
        {
            UpdateHeaderReferenceButtons();
        }
        else if (e.PropertyName == nameof(ViewModel.BranchScope))
        {
            SynchronizeBranchScope();
        }
        else if (e.PropertyName == nameof(ViewModel.SelectedBranch))
        {
            await LoadOpenBranchCardsAsync();
        }
        else if (e.PropertyName == nameof(ViewModel.SelectedTag))
        {
            await LoadOpenTagCardsAsync();
        }
    }

    private void HeaderToggleButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }

    private void HeaderToggleButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = null;
    }

    private void UpdateHeaderReferenceButtons()
    {
        bool branchesMode = ViewModel.ReferenceKind == ReferenceListKind.Branches;

        BranchesHeaderToggleButton.IsChecked = branchesMode;
        TagsHeaderToggleButton.IsChecked = !branchesMode;
        BranchesHeaderTextBlock.Foreground = GetHeaderBrush(branchesMode);
        TagsHeaderTextBlock.Foreground = GetHeaderBrush(!branchesMode);
    }

    private static Brush GetHeaderBrush(bool isActive)
    {
        string resourceKey = isActive
            ? "BranchesPageHeaderActiveBrush"
            : "BranchesPageHeaderInactiveBrush";

        return ThemeResourceResolver.GetBrush(resourceKey);
    }

    private async void BranchScopeSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_synchronizingBranchScope || sender.SelectedItem?.Tag is not string scope)
        {
            return;
        }

        ViewModel.BranchScope = scope switch
        {
            "Remote" => BranchListScope.Remote,
            _ => BranchListScope.Local
        };

        if (ViewModel.ReferenceKind == ReferenceListKind.Tags && ViewModel.BranchScope == BranchListScope.Remote)
        {
            await ViewModel.EnsureRemoteTagsLoadedAsync();
        }
    }

    private void BranchItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ReferenceTreeItemViewModel { Branch: GitBranch branch } item })
        {
            BranchesTreeView.SelectedItem = item;
            ViewModel.SelectedBranch = branch;
        }
    }

    private void ReferenceTreeItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem item) return;
        if (IsOnSelectedPath(ViewModel.SelectedBranchTreeItem)
            || IsOnSelectedPath(ViewModel.SelectedTagTreeItem)) { QueueReferenceSelection(); }
        if (ReferenceEquals(item.DataContext, _referenceToReveal)) { QueueReferenceReveal(); }

        // The native template fixes glyphs at 12x12 with padding and gives the chevron
        // 14px side padding. Resize those parts without replacing its interaction states.
        item.ApplyTemplate();
        if (FindTemplatePart(item, "ExpandCollapseChevron") is Grid chevron)
        {
            chevron.Padding = new Thickness(4, 0, 4, 0);
            foreach (UIElement child in chevron.Children)
            {
                if (child is TextBlock glyph)
                {
                    glyph.Width = 16;
                    glyph.Height = 16;
                    glyph.Padding = new Thickness(0);
                    glyph.TextAlignment = TextAlignment.Center;
                }
            }
        }

        static FrameworkElement? FindTemplatePart(DependencyObject parent, string name)
        {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                if (child is FrameworkElement element && element.Name == name) return element;
                FrameworkElement? match = FindTemplatePart(child, name);
                if (match is not null) return match;
            }
            return null;
        }

        bool IsOnSelectedPath(ReferenceTreeItemViewModel? selected)
        {
            for (ReferenceTreeItemViewModel? reference = selected; reference is not null; reference = reference.Parent)
            {
                if (ReferenceEquals(item.DataContext, reference)) { return true; }
            }
            return false;
        }
    }

    private void ReferenceFolder_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ReferenceTreeItemViewModel { IsFolder: true } folder })
        {
            folder.ToggleFolderExpansionCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void TagItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ReferenceTreeItemViewModel { Tag: GitTag tag } item })
        {
            TagsTreeView.SelectedItem = item;
            ViewModel.SelectedTag = tag;
        }
    }

    private void BranchTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (_synchronizingReferenceSelection || !IsLoaded) { return; }
        // SelectedItem can still contain the previous selection while this event is raised.
        foreach (object addedItem in args.AddedItems)
        {
            object item = addedItem is TreeViewNode node ? node.Content : addedItem;
            if (item is ReferenceTreeItemViewModel { Branch: GitBranch branch }
                && ViewModel.FilteredBranches.Contains(branch))
            {
                ViewModel.SelectedBranch = branch;
            }
        }
    }

    private void TagTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (_synchronizingReferenceSelection || !IsLoaded) { return; }
        foreach (object addedItem in args.AddedItems)
        {
            object item = addedItem is TreeViewNode node ? node.Content : addedItem;
            if (item is ReferenceTreeItemViewModel { Tag: GitTag tag }
                && ViewModel.FilteredTags.Contains(tag))
            {
                ViewModel.SelectedTag = tag;
            }
        }
    }

    private void PaneSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (LayoutStates.CurrentState?.Name == "NarrowLayout")
        {
            ResizeNarrowPanes(e.VerticalChange);
            return;
        }

        ResizeWidePanes(e.HorizontalChange);
    }

    private void ResizeNarrowPanes(double verticalChange)
    {
        double requestedHeight = ListRow.ActualHeight + verticalChange;
        double availableHeight = ListRow.ActualHeight + DetailsRow.ActualHeight;

        if (availableHeight <= 0)
        {
            return;
        }

        double listHeight = Math.Clamp(requestedHeight, 0, availableHeight);
        ListRow.Height = new GridLength(listHeight, GridUnitType.Star);
        DetailsRow.Height = new GridLength(availableHeight - listHeight, GridUnitType.Star);
    }

    private void ResizeWidePanes(double horizontalChange)
    {
        double requestedWidth = ListColumn.ActualWidth + horizontalChange;
        double availableWidth = ListColumn.ActualWidth + DetailsColumn.ActualWidth;

        if (availableWidth <= 0)
        {
            return;
        }

        double listWidth = Math.Clamp(requestedWidth, 0, availableWidth);
        ListColumn.Width = new GridLength(listWidth, GridUnitType.Star);
        DetailsColumn.Width = new GridLength(availableWidth - listWidth, GridUnitType.Star);
    }

    private async void OpenLastCommitChangesCard_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.EnsureSelectedBranchCommitLoadedAsync();
        CommitDiffNavigationArgs? arguments = ViewModel.CreateCommitChangesDiffArgs();
        if (arguments is not null)
        {
            Frame.Navigate(typeof(CommitRangePage), arguments);
        }
    }

    private void BranchCommitHistoryCard_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenSelectedBranchCommitHistoryCommand.Execute(null);
    }

    private async void BranchSynchronizationExpander_Expanded(object sender, EventArgs e)
    {
        await ViewModel.EnsureBranchSynchronizationLoadedAsync();
    }

    private async void BranchCommitExpander_Expanded(object sender, EventArgs e)
    {
        await ViewModel.EnsureSelectedBranchCommitLoadedAsync();
    }

    private async void BranchComparisonExpander_Expanded(object sender, EventArgs e)
    {
        await ViewModel.EnsureSelectedBranchComparisonLoadedAsync();
    }

    private async void BranchWorktreeExpander_Expanded(object sender, EventArgs e)
    {
        await ViewModel.EnsureSelectedBranchWorktreesLoadedAsync();
    }

    private async void BranchHistoryExpander_Expanded(object sender, EventArgs e)
    {
        await ViewModel.EnsureSelectedBranchHistoryLoadedAsync();
    }

    private async void TagInformationExpander_Expanded(object sender, EventArgs e)
    {
        await ViewModel.EnsureSelectedTagDetailsLoadedAsync();
    }

    private void TagTargetObjectCard_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenSelectedTagTargetCommitCommand.Execute(null);
    }

    private async void TagRelationExpander_Expanded(object sender, EventArgs e)
    {
        await ViewModel.EnsureSelectedTagRelationLoadedAsync();
    }

    private async void TagSignatureExpander_Expanded(object sender, EventArgs e)
    {
        await ViewModel.EnsureSelectedTagSignatureLoadedAsync();
    }

    private async void TagWorktreeExpander_Expanded(object sender, EventArgs e)
    {
        await ViewModel.EnsureSelectedTagWorktreesLoadedAsync();
    }

    private Task LoadOpenBranchCardsAsync()
    {
        List<Task> tasks = [];
        if (BranchSynchronizationExpander.IsExpanded) tasks.Add(ViewModel.EnsureBranchSynchronizationLoadedAsync());
        if (BranchLastCommitExpander.IsExpanded) tasks.Add(ViewModel.EnsureSelectedBranchCommitLoadedAsync());
        if (BranchComparisonExpander.IsExpanded) tasks.Add(ViewModel.EnsureSelectedBranchComparisonLoadedAsync());
        if (BranchHistoryExpander.IsExpanded) tasks.Add(ViewModel.EnsureSelectedBranchHistoryLoadedAsync());
        if (BranchWorktreeExpander.IsExpanded) tasks.Add(ViewModel.EnsureSelectedBranchWorktreesLoadedAsync());
        return Task.WhenAll(tasks);
    }

    private Task LoadOpenTagCardsAsync()
    {
        List<Task> tasks = [];
        if (TagInformationExpander.IsExpanded) tasks.Add(ViewModel.EnsureSelectedTagDetailsLoadedAsync());
        if (TagSignatureExpander.IsExpanded) tasks.Add(ViewModel.EnsureSelectedTagSignatureLoadedAsync());
        if (TagRelationExpander.IsExpanded) tasks.Add(ViewModel.EnsureSelectedTagRelationLoadedAsync());
        if (TagWorktreesExpander.IsExpanded) tasks.Add(ViewModel.EnsureSelectedTagWorktreesLoadedAsync());
        return Task.WhenAll(tasks);
    }

}
