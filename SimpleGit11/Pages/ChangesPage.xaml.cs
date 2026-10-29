using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using SimpleGit11.Models;
using SimpleGit11.Presentation.Navigation;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Pages;

public sealed partial class ChangesPage : Page, IPageRefreshTarget
{
    private bool _synchronizingFileSelection;

    public ChangesPage()
    {
        ViewModel = App.GetService<ChangesViewModel>();
        InitializeComponent();
        ChangedFilesTree.FileContextMenu = ChangesFileContextMenu;
        Loaded += (_, _) =>
        {
            ViewModel.FileTree.PropertyChanged += FileTree_PropertyChanged;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            RestoreFileSelection();
        };
        Unloaded += (_, _) =>
        {
            ViewModel.FileTree.PropertyChanged -= FileTree_PropertyChanged;
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        };
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
        if (_synchronizingFileSelection || ViewModel.FileTree.ShowFolders) { return; }

        System.Collections.Generic.List<GitChangedFile> selectedChanges = ChangedFilesListView.SelectedItems
            .OfType<GitChangedFile>()
            .ToList();
        GitChangedFile? lastSelectedChange = e.AddedItems
            .OfType<GitChangedFile>()
            .LastOrDefault()
            ?? selectedChanges.LastOrDefault();

        ViewModel.SetSelectedChanges(selectedChanges, lastSelectedChange);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ChangesViewModel.IsRefreshing) && !ViewModel.IsRefreshing)
        { RestoreFileSelection(); }
    }

    private void FileTree_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ChangedFileTreeViewModel.ShowFolders) || ViewModel.FileTree.ShowFolders) { return; }
        RestoreFileSelection();
    }

    private void RestoreFileSelection()
    {
        GitChangedFile[] selected = ViewModel.SelectedChanges.ToArray();
        _synchronizingFileSelection = true;
        try
        {
            ChangedFilesListView.SelectedItems.Clear();
            foreach (GitChangedFile file in selected) { ChangedFilesListView.SelectedItems.Add(file); }
        }
        finally { _synchronizingFileSelection = false; }
    }

    private void ChangedFiles_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        GitChangedFile? file = Controls.ChangedFilesTree.GetContextFile(args);
        if (file is null && !args.TryGetPosition(ChangedFilesListView, out _))
        {
            file = ChangedFilesListView.SelectedItem as GitChangedFile;
        }
        if (file is not null)
        {
            Controls.ChangedFilesTree.ShowFileContextMenu(ChangesFileContextMenu, file, ChangedFilesListView, args);
        }
        else { args.Handled = true; }
    }

    private void ChangedFiles_ContextCanceled(UIElement sender, RoutedEventArgs args)
    {
        ChangesFileContextMenu.Hide();
    }

    private void ChangedFilesTree_FileSelectionChanged(object? sender, EventArgs args)
    {
        ViewModel.SetSelectedChanges(ChangedFilesTree.SelectedFiles ?? [], ChangedFilesTree.SelectedFile);
    }

    private void ChangedFilesFilterToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button)
        {
            button.IsChecked = ViewModel.IsFileFilterApplied;
            FlyoutBase.ShowAttachedFlyout(button);
        }
    }
}
