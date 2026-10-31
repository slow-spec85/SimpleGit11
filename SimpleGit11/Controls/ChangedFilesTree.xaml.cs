using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SimpleGit11.Models;
using SimpleGit11.ViewModels;
using Windows.System;

namespace SimpleGit11.Controls;

public sealed partial class ChangedFilesTree : UserControl
{
    private bool _synchronizingSelection;
    private bool _refreshingItems;

    public ChangedFilesTree()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            SubscribeToModel();
            QueueSelectionSynchronization();
        };
        Unloaded += (_, _) => UnsubscribeFromModel(Model);
        FileTree.Loaded += (_, _) =>
        {
            UpdateContainerPadding();
            QueueSelectionSynchronization();
        };
        RegisterPropertyChangedCallback(PaddingProperty, (_, _) => UpdateContainerPadding());
    }

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(ChangedFileTreeViewModel), typeof(ChangedFilesTree),
        new PropertyMetadata(null, OnModelChanged));

    public static readonly DependencyProperty SelectedFileProperty = DependencyProperty.Register(
        nameof(SelectedFile), typeof(GitChangedFile), typeof(ChangedFilesTree),
        new PropertyMetadata(null, OnSelectionChanged));

    public static readonly DependencyProperty SelectedFilesProperty = DependencyProperty.Register(
        nameof(SelectedFiles), typeof(IReadOnlyList<GitChangedFile>), typeof(ChangedFilesTree),
        new PropertyMetadata(null, OnSelectionChanged));

    public ChangedFileTreeViewModel? Model
    {
        get => (ChangedFileTreeViewModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public GitChangedFile? SelectedFile
    {
        get => (GitChangedFile?)GetValue(SelectedFileProperty);
        set => SetValue(SelectedFileProperty, value);
    }

    public IReadOnlyList<GitChangedFile>? SelectedFiles
    {
        get => (IReadOnlyList<GitChangedFile>?)GetValue(SelectedFilesProperty);
        set => SetValue(SelectedFilesProperty, value);
    }

    public ListViewSelectionMode SelectionMode
    {
        get => FileTree.SelectionMode;
        set => FileTree.SelectionMode = value;
    }

    public static readonly DependencyProperty ColumnMetricsProperty = DependencyProperty.Register(
        nameof(ColumnMetrics), typeof(DiffStatColumnMetrics), typeof(ChangedFilesTree), new PropertyMetadata(null));

    public DiffStatColumnMetrics? ColumnMetrics
    {
        get => (DiffStatColumnMetrics?)GetValue(ColumnMetricsProperty);
        set => SetValue(ColumnMetricsProperty, value);
    }

    public static readonly DependencyProperty ContentPaddingProperty = DependencyProperty.Register(
        nameof(ContentPadding), typeof(Thickness), typeof(ChangedFilesTree),
        new PropertyMetadata(default(Thickness), OnContentPaddingChanged));

    public Thickness ContentPadding
    {
        get => (Thickness)GetValue(ContentPaddingProperty);
        set => SetValue(ContentPaddingProperty, value);
    }

    public MenuFlyout? FileContextMenu { get; set; }

    public static readonly DependencyProperty CopyTextCommandProperty = DependencyProperty.Register(
        nameof(CopyTextCommand), typeof(ICommand), typeof(ChangedFilesTree), new PropertyMetadata(null));

    public ICommand? CopyTextCommand
    {
        get => (ICommand?)GetValue(CopyTextCommandProperty);
        set => SetValue(CopyTextCommandProperty, value);
    }

    public event EventHandler? FileSelectionChanged;

    public static Thickness GetContentPadding(Thickness headerPadding, Thickness statisticsMargin)
    {
        return new Thickness(0, 0, headerPadding.Right + statisticsMargin.Right, 0);
    }

    public static Thickness GetItemPadding(Thickness contentPadding, Thickness listPadding)
    {
        return new Thickness(0, 0, Math.Max(0, contentPadding.Right - listPadding.Right), 0);
    }

    private static void OnContentPaddingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((ChangedFilesTree)sender).UpdateContainerPadding();
    }

    private void UpdateContainerPadding()
    {
        if (FileTree.ItemsPanelRoot is not Panel panel) { return; }
        Thickness padding = GetItemPadding(ContentPadding, Padding);
        foreach (SelectorItem container in panel.Children.OfType<SelectorItem>())
        {
            container.Padding = padding;
        }
    }

    private void FileTree_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue)
        {
            args.ItemContainer.Padding = GetItemPadding(ContentPadding, Padding);
        }
    }

    private void StatPresenter_Loaded(object sender, RoutedEventArgs args)
    {
        // Template rows have their own namescope; share the header metrics explicitly.
        ((DiffStatPresenter)sender).ColumnMetrics = ColumnMetrics;
    }

    private static void OnModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ChangedFilesTree view = (ChangedFilesTree)sender;
        view.UnsubscribeFromModel(args.OldValue as ChangedFileTreeViewModel);
        if (view.IsLoaded) { view.SubscribeToModel(); }
        view.QueueSelectionSynchronization();
    }

    private void SubscribeToModel()
    {
        if (Model is null) { return; }
        UnsubscribeFromModel(Model);
        Model.PropertyChanged += Model_PropertyChanged;
        Model.PropertyChanging += Model_PropertyChanging;
    }

    private void UnsubscribeFromModel(ChangedFileTreeViewModel? model)
    {
        if (model is null) { return; }
        model.PropertyChanged -= Model_PropertyChanged;
        model.PropertyChanging -= Model_PropertyChanging;
    }

    private void Model_PropertyChanging(object? sender, PropertyChangingEventArgs args)
    {
        if (args.PropertyName == nameof(ChangedFileTreeViewModel.Roots)) { _refreshingItems = true; }
    }

    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ChangedFileTreeViewModel.Roots) or nameof(ChangedFileTreeViewModel.ShowFolders))
        {
            QueueSelectionSynchronization();
        }
    }

    private static void OnSelectionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((ChangedFilesTree)sender).SynchronizeSelection();
    }

    private void QueueSelectionSynchronization()
    {
        // Run after the ItemsSource binding has replaced the old hierarchy.
        DispatcherQueue.TryEnqueue(() => { _refreshingItems = false; SynchronizeSelection(); });
    }

    private void SynchronizeSelection()
    {
        if (_synchronizingSelection || _refreshingItems || !FileTree.IsLoaded
            || Model is not { ShowFolders: true } model)
        {
            return;
        }

        _synchronizingSelection = true;
        try
        {
            GitChangedFile[] selected = (SelectionMode == ListViewSelectionMode.Extended ? SelectedFiles?.ToArray() : null)
                ?? (SelectedFile is GitChangedFile file ? [file] : []);
            UpdateSelectedItems(
                FileTree.SelectedItems, model.VisibleItems, selected,
                SelectionMode, item => FileTree.SelectedItem = item);
        }
        finally { _synchronizingSelection = false; }
    }

    private static void UpdateSelectedItems(
        IList<object>? selectedItems,
        IEnumerable<ChangedFileTreeItemViewModel> visibleItems,
        IReadOnlyCollection<GitChangedFile> selectedFiles,
        ListViewSelectionMode selectionMode,
        Action<object?> setSelectedItem)
    {
        if (selectionMode == ListViewSelectionMode.None) { return; }
        HashSet<GitChangedFile> requested = selectedFiles.ToHashSet();
        ChangedFileTreeItemViewModel[] matches = visibleItems
            .Where(item => item.File is not null && requested.Contains(item.File)).ToArray();
        // WinUI's SelectedItems vector cannot be modified in single-selection mode.
        if (selectionMode == ListViewSelectionMode.Single)
        {
            setSelectedItem(matches.FirstOrDefault());
            return;
        }
        // Keep the bound selection intact while the list is being initialized.
        if (selectedItems is null) { return; }
        HashSet<ChangedFileTreeItemViewModel> remaining = matches.ToHashSet();
        // A binding echo must not clear ListView's native Ctrl/Shift selection anchor.
        for (int index = selectedItems.Count - 1; index >= 0; index--)
        {
            if (selectedItems[index] is not ChangedFileTreeItemViewModel item || !remaining.Contains(item))
            {
                selectedItems.RemoveAt(index);
            }
        }
        HashSet<object> current = selectedItems.ToHashSet();
        foreach (ChangedFileTreeItemViewModel item in matches)
        {
            if (current.Add(item)) { selectedItems.Add(item); }
        }
    }

    private void FileTree_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_synchronizingSelection || _refreshingItems || Model?.ShowFolders != true) { return; }

        GitChangedFile[] files = FileTree.SelectedItems.OfType<ChangedFileTreeItemViewModel>()
            .Where(item => item.File is not null).Select(item => item.File!).ToArray();
        GitChangedFile? focused = args.AddedItems.OfType<ChangedFileTreeItemViewModel>()
            .LastOrDefault(item => item.File is not null)?.File ?? files.LastOrDefault();
        _synchronizingSelection = true;
        try
        {
            SelectedFiles = files;
            SelectedFile = focused;
        }
        finally { _synchronizingSelection = false; }
        FileSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void FileTree_ItemClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is ChangedFileTreeItemViewModel { IsFolder: true } item)
        {
            item.ToggleFolderExpansionCommand.Execute(null);
        }
    }

    private void FileTree_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (FileTree.SelectedItem is not ChangedFileTreeItemViewModel item
            || ((Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)
                    | Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                    | Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift))
                & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0)
        {
            return;
        }

        if (args.Key == VirtualKey.Right && item.IsFolder)
        {
            if (!item.IsExpanded) { item.IsExpanded = true; }
            else if (item.Children.FirstOrDefault() is ChangedFileTreeItemViewModel child)
            {
                FileTree.SelectedItem = child;
                FileTree.ScrollIntoView(child);
            }
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Left)
        {
            if (item.IsFolder && item.IsExpanded) { item.IsExpanded = false; }
            else if (item.Parent is ChangedFileTreeItemViewModel parent)
            {
                FileTree.SelectedItem = parent;
                FileTree.ScrollIntoView(parent);
            }
            args.Handled = true;
        }
    }

    private void FileItem_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: ChangedFileTreeItemViewModel item } element)
        {
            ShowItemContextMenu(item, element, args);
        }
    }

    private void FileTree_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (args.Handled) { return; }
        ChangedFileTreeItemViewModel? item = null;
        FrameworkElement target = FileTree;
        for (DependencyObject? current = args.OriginalSource as DependencyObject;
             current is not null && !ReferenceEquals(current, FileTree);
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is ListViewItem container)
            {
                item = FileTree.ItemFromContainer(container) as ChangedFileTreeItemViewModel;
                target = container;
                break;
            }
        }
        if (item is null && !args.TryGetPosition(FileTree, out _))
        {
            item = FileTree.SelectedItem as ChangedFileTreeItemViewModel;
        }
        ShowItemContextMenu(item, target, args);
    }

    private void FileTree_ContextCanceled(UIElement sender, RoutedEventArgs args)
    {
        FolderContextMenu.Hide();
        FileContextMenu?.Hide();
    }

    private void ShowItemContextMenu(
        ChangedFileTreeItemViewModel? item, FrameworkElement target, ContextRequestedEventArgs args)
    {
        if (item?.File is GitChangedFile file)
        {
            if (FileContextMenu is MenuFlyout menu) { ShowFileContextMenu(menu, file, target, args); }
        }
        else if (item?.IsFolder == true)
        {
            ExpandAllFoldersMenuItem.CommandParameter = item;
            CollapseAllFoldersMenuItem.CommandParameter = item;
            ExpandAllFoldersMenuItem.Visibility = Model?.ExpandAllFoldersCommand.CanExecute(item) == true ? Visibility.Visible : Visibility.Collapsed;
            CollapseAllFoldersMenuItem.Visibility = Model?.CollapseAllFoldersCommand.CanExecute(item) == true ? Visibility.Visible : Visibility.Collapsed;
            CopyFolderNameMenuItem.CommandParameter = item.Name;
            if (args.TryGetPosition(target, out Windows.Foundation.Point position)) { FolderContextMenu.ShowAt(target, position); }
            else { FolderContextMenu.ShowAt(target); }
        }
        args.Handled = true;
    }

    // Both list modes use the very same command menu and original Git file instance.
    public static void ShowFileContextMenu(
        MenuFlyout menu, GitChangedFile file, FrameworkElement target, ContextRequestedEventArgs args)
    {
        foreach (MenuFlyoutItem command in menu.Items.OfType<MenuFlyoutItem>())
        {
            command.CommandParameter = Equals(command.Tag, "CopyName") ? file.FileName : file;
            if (Equals(command.Tag, "FileContent")) { command.Visibility = file.CanShowFileContent ? Visibility.Visible : Visibility.Collapsed; }
            if (Equals(command.Tag, "Stage")) { command.Visibility = file.IsUnstaged ? Visibility.Visible : Visibility.Collapsed; }
            if (Equals(command.Tag, "Unstage")) { command.Visibility = file.IsStaged ? Visibility.Visible : Visibility.Collapsed; }
            if (Equals(command.Tag, "Discard")) { command.Visibility = file.CanDiscard ? Visibility.Visible : Visibility.Collapsed; }
            if (Equals(command.Tag, "Ignore")) { command.Visibility = file.IsUntracked ? Visibility.Visible : Visibility.Collapsed; }
        }
        foreach (MenuFlyoutSeparator separator in menu.Items.OfType<MenuFlyoutSeparator>()
            .Where(separator => Equals(separator.Tag, "ChangeActions")))
        {
            separator.Visibility = file.IsUnstaged || file.IsStaged || file.CanDiscard || file.IsUntracked
                ? Visibility.Visible : Visibility.Collapsed;
        }
        if (args.TryGetPosition(target, out Windows.Foundation.Point position))
        {
            menu.ShowAt(target, position);
        }
        else { menu.ShowAt(target); }
        args.Handled = true;
    }

    public static GitChangedFile? GetContextFile(ContextRequestedEventArgs args)
    {
        for (DependencyObject? current = args.OriginalSource as DependencyObject;
             current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: GitChangedFile file }) { return file; }
        }
        return null;
    }
}
