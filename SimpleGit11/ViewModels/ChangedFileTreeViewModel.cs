using SimpleGit11.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.UI.Xaml;
using SimpleGit11.Models;

namespace SimpleGit11.ViewModels;

// Shared hierarchy and view mode for the history and working tree file lists.
public sealed partial class ChangedFileTreeViewModel : ObservableObject
{
    private bool _updatingExpansion;

    [RelayCommand(CanExecute = nameof(CanExpandAllFolders))]
    private void OnExpandAllFolders(ChangedFileTreeItemViewModel? folder) => SetFolderExpansion(folder, true);

    [RelayCommand(CanExecute = nameof(CanCollapseAllFolders))]
    private void OnCollapseAllFolders(ChangedFileTreeItemViewModel? folder) => SetFolderExpansion(folder, false);

    private bool CanExpandAllFolders(ChangedFileTreeItemViewModel? folder) => folder is { IsFolder: true, IsExpanded: false };
    private bool CanCollapseAllFolders(ChangedFileTreeItemViewModel? folder) => folder is { IsFolder: true, IsExpanded: true };

    private void SetFolderExpansion(ChangedFileTreeItemViewModel? folder, bool expanded)
    {
        if (folder?.IsFolder != true) { return; }
        _updatingExpansion = true;
        try
        {
            foreach (ChangedFileTreeItemViewModel item in EnumerateFolders([folder])) { item.IsExpanded = expanded; }
        }
        finally
        {
            _updatingExpansion = false;
            RefreshVisibleItems();
            ExpandAllFoldersCommand.NotifyCanExecuteChanged();
            CollapseAllFoldersCommand.NotifyCanExecuteChanged();
        }
    }

    public ChangedFileTreeViewModel(bool showFolders = false, IMessenger? messenger = null)
    {
        ShowFolders = showFolders;
        messenger?.Register<ChangedFileTreeViewModel, PropertyChangedMessage<bool>>(this, static (recipient, message) =>
        {
            if (message.PropertyName == nameof(AppSettings.ShowFileFolders)) { recipient.ShowFolders = message.NewValue; }
        });
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FlatListVisibility))]
    [NotifyPropertyChangedFor(nameof(TreeVisibility))]
    public partial bool ShowFolders { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ChangedFileTreeItemViewModel> Roots { get; private set; } = [];

    public ObservableCollection<ChangedFileTreeItemViewModel> VisibleItems { get; } = [];

    partial void OnRootsChanging(IReadOnlyList<ChangedFileTreeItemViewModel> value)
    {
        foreach (ChangedFileTreeItemViewModel item in EnumerateFolders(Roots))
        {
            item.PropertyChanged -= Folder_PropertyChanged;
        }
    }

    partial void OnRootsChanged(IReadOnlyList<ChangedFileTreeItemViewModel> value)
    {
        foreach (ChangedFileTreeItemViewModel item in EnumerateFolders(value))
        {
            item.PropertyChanged += Folder_PropertyChanged;
        }
        RefreshVisibleItems();
    }

    private static IEnumerable<ChangedFileTreeItemViewModel> EnumerateFolders(
        IEnumerable<ChangedFileTreeItemViewModel> items)
    {
        foreach (ChangedFileTreeItemViewModel item in items.Where(item => item.IsFolder))
        {
            yield return item;
            foreach (ChangedFileTreeItemViewModel child in EnumerateFolders(item.Children)) { yield return child; }
        }
    }

    private void Folder_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_updatingExpansion && args.PropertyName == nameof(ChangedFileTreeItemViewModel.IsExpanded))
        {
            RefreshVisibleItems();
            ExpandAllFoldersCommand.NotifyCanExecuteChanged();
            CollapseAllFoldersCommand.NotifyCanExecuteChanged();
        }
    }

    private void RefreshVisibleItems()
    {
        List<ChangedFileTreeItemViewModel> visible = [];
        AppendVisible(Roots);
        HashSet<ChangedFileTreeItemViewModel> remaining = visible.ToHashSet();
        // Preserve existing rows so expanding a folder keeps ListView selection and range anchors.
        for (int index = VisibleItems.Count - 1; index >= 0; index--)
        {
            if (!remaining.Contains(VisibleItems[index])) { VisibleItems.RemoveAt(index); }
        }
        for (int index = 0; index < visible.Count; index++)
        {
            if (index >= VisibleItems.Count || !ReferenceEquals(VisibleItems[index], visible[index]))
            {
                VisibleItems.Insert(index, visible[index]);
            }
        }

        void AppendVisible(IEnumerable<ChangedFileTreeItemViewModel> items)
        {
            foreach (ChangedFileTreeItemViewModel item in items)
            {
                visible.Add(item);
                if (item.IsFolder && item.IsExpanded) { AppendVisible(item.Children); }
            }
        }
    }

    public Visibility FlatListVisibility => ShowFolders ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TreeVisibility => ShowFolders ? Visibility.Visible : Visibility.Collapsed;

    private GitChangedFile[] _files = [];

    public void Refresh(IEnumerable<GitChangedFile> files)
    {
        GitChangedFile[] snapshot = files.ToArray();
        if (_files.SequenceEqual(snapshot)) { return; }
        _files = snapshot;
        files = snapshot;
        Dictionary<string, bool> expansion = EnumerateFolders(Roots)
            .ToDictionary(item => item.Path, item => item.IsExpanded, StringComparer.Ordinal);
        List<ChangedFileTreeItemViewModel> roots = [];
        Dictionary<string, ChangedFileTreeItemViewModel> folders = new(StringComparer.Ordinal);
        foreach (GitChangedFile file in files)
        {
            string[] segments = file.Path.Replace('\\', '/').Split('/');
            List<ChangedFileTreeItemViewModel> siblings = roots;
            string path = "";
            ChangedFileTreeItemViewModel? parent = null;
            foreach (string segment in segments[..^1])
            {
                path = path.Length == 0 ? segment : $"{path}/{segment}";
                if (!folders.TryGetValue(path, out ChangedFileTreeItemViewModel? folder))
                {
                    folder = new(segment, path, parent: parent) { IsExpanded = expansion.GetValueOrDefault(path, true) };
                    folders.Add(path, folder);
                    siblings.Add(folder);
                }
                siblings = folder.Children;
                parent = folder;
            }
            // Staged and unstaged entries for the same path remain distinct leaves.
            siblings.Add(new(file.FileName, file.Path, file, parent));
        }
        Sort(roots);
        foreach (ChangedFileTreeItemViewModel folder in EnumerateFolders(Roots)) { folder.PropertyChanged -= Folder_PropertyChanged; }
        IReadOnlyList<ChangedFileTreeItemViewModel> snapshotRoots = Reconcile(Roots, roots, null);
        if (ReferenceEquals(Roots, snapshotRoots))
        {
            foreach (ChangedFileTreeItemViewModel folder in EnumerateFolders(Roots)) { folder.PropertyChanged += Folder_PropertyChanged; }
            RefreshVisibleItems();
        }
        else { Roots = snapshotRoots; }

        static IReadOnlyList<ChangedFileTreeItemViewModel> Reconcile(
            IReadOnlyList<ChangedFileTreeItemViewModel> existing, IEnumerable<ChangedFileTreeItemViewModel> incoming,
            ChangedFileTreeItemViewModel? parent)
        {
            Dictionary<(string, bool, GitChangeState?), ChangedFileTreeItemViewModel> byKey = existing
                .ToDictionary(item => (item.Path, item.IsFolder, item.File?.State));
            List<ChangedFileTreeItemViewModel> result = [];
            foreach (ChangedFileTreeItemViewModel item in incoming)
            {
                byKey.TryGetValue((item.Path, item.IsFolder, item.File?.State), out ChangedFileTreeItemViewModel? previous);
                ChangedFileTreeItemViewModel node = previous is not null && ReferenceEquals(previous.Parent, parent)
                    && ReferenceEquals(previous.File, item.File) ? previous
                    : new ChangedFileTreeItemViewModel(item.Name, item.Path, item.File, parent) { IsExpanded = item.IsExpanded };
                IReadOnlyList<ChangedFileTreeItemViewModel> children = Reconcile(node.Children, item.Children, node);
                if (!ReferenceEquals(node.Children, children))
                {
                    node.Children.Clear();
                    node.Children.AddRange(children);
                }
                node.UpdateFolderStat();
                result.Add(node);
            }
            return CollectionSynchronization.ReuseItems(existing, result, item => (item.Path, item.IsFolder, item.File?.State));
        }

        static void Sort(List<ChangedFileTreeItemViewModel> items)
        {
            // Stable sorting preserves the state order of duplicate file paths.
            ChangedFileTreeItemViewModel[] sorted = items.OrderBy(item => item.IsFile)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.Ordinal).ToArray();
            items.Clear();
            items.AddRange(sorted);
            foreach (ChangedFileTreeItemViewModel item in items) { Sort(item.Children); }
        }
    }
}
