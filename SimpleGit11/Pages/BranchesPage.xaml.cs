using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SimpleGit11.Presentation.Theming;
using SimpleGit11.Presentation.Navigation;
using SimpleGit11.Services;
using SimpleGit11.ViewModels;
using SimpleGit11.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SimpleGit11.Pages;

public sealed partial class BranchesPage : Page, IPageRefreshTarget, IRemoteSelectionRefreshPage
{
    public BranchesViewModel ViewModel { get; }

    public BranchesPage()
    {
        ViewModel = App.GetService<BranchesViewModel>();
        InitializeComponent();
        BranchScopeSelectorBar.SelectedItem = LocalBranchScopeSelectorBarItem;
        UpdateHeaderReferenceButtons();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateHeaderReferenceButtons();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    public Task RefreshAsync()
    {
        return ViewModel.RefreshBranchesLocalAsync();
    }

    public Task RefreshSelectedRemoteAsync()
    {
        return ViewModel.RefreshSelectedRemoteAsync();
    }

    private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.ReferenceKind))
        {
            UpdateHeaderReferenceButtons();
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
        if (sender.SelectedItem?.Tag is not string scope)
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
        if (sender is FrameworkElement { DataContext: GitBranch branch })
        {
            ViewModel.SelectedBranch = branch;
        }
    }

    private void TagItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GitTag tag })
        {
            ViewModel.SelectedTag = tag;
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
