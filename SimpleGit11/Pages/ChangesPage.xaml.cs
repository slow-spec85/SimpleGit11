using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SimpleGit11.Models;
using SimpleGit11.Presentation.Navigation;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Pages;

public sealed partial class ChangesPage : Page, IPageRefreshTarget
{
    public ChangesPage()
    {
        ViewModel = App.GetService<ChangesViewModel>();
        InitializeComponent();
    }

    public ChangesViewModel ViewModel { get; }

    public Task RefreshAsync()
    {
        return ViewModel.RefreshStatusAsync();
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
        double requestedHeight = FileListsRow.ActualHeight + verticalChange;
        double availableHeight = FileListsRow.ActualHeight + DiffRow.ActualHeight;

        if (availableHeight <= 0)
        {
            return;
        }

        double listsHeight = Math.Clamp(requestedHeight, 0, availableHeight);
        FileListsRow.Height = new GridLength(listsHeight, GridUnitType.Star);
        DiffRow.Height = new GridLength(availableHeight - listsHeight, GridUnitType.Star);
    }

    private void ResizeWideDiffPanels(double horizontalChange)
    {
        double requestedWidth = FileListsColumn.ActualWidth + horizontalChange;
        double availableWidth = FileListsColumn.ActualWidth + DiffColumn.ActualWidth;
        double maxListsWidth = Math.Max(0, availableWidth - DiffColumn.MinWidth);

        if (availableWidth <= 0)
        {
            return;
        }

        double listsWidth = Math.Clamp(requestedWidth, 0, maxListsWidth);
        FileListsColumn.Width = new GridLength(listsWidth, GridUnitType.Star);
        DiffColumn.Width = new GridLength(availableWidth - listsWidth, GridUnitType.Star);
    }

    private void ChangedFilesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        System.Collections.Generic.List<GitChangedFile> selectedChanges = ChangedFilesListView.SelectedItems
            .OfType<GitChangedFile>()
            .ToList();
        GitChangedFile? lastSelectedChange = e.AddedItems
            .OfType<GitChangedFile>()
            .LastOrDefault()
            ?? selectedChanges.LastOrDefault();

        ViewModel.SetSelectedChanges(selectedChanges, lastSelectedChange);
    }
}
