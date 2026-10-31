using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using SimpleGit11.Models;

namespace SimpleGit11.ViewModels;

// Directory expansion belongs to the presentation, rather than to the Git file model.
public sealed partial class ChangedFileTreeItemViewModel(
    string name, string path, GitChangedFile? file = null, ChangedFileTreeItemViewModel? parent = null) : ObservableObject
{
    private DiffStat _folderStat = DiffStat.Empty;
    public string Name { get; } = name;
    public string Path { get; } = path;
    public ChangedFileTreeItemViewModel? Parent { get; } = parent;
    public int Depth => Parent is null ? 0 : Parent.Depth + 1;
    public Thickness Indentation => new(Depth * 16, 0, 0, 0);
    public string ExpansionGlyph => IsExpanded ? "\uE70D" : "\uE76C";
    public GitChangedFile? File { get; } = file;
    public bool IsFolder => File is null;
    public bool IsFile => File is not null;
    public string StatusIndicator => File?.StatusIndicator ?? "";
    public string StateLabel => File?.StateLabel ?? "";
    public bool IsStaged => File?.IsStaged == true;
    public bool IsSubmodule => File?.IsSubmodule == true;
    public bool IsConflicted => File?.IsConflicted == true;
    public string Tooltip => File?.Tooltip ?? Path;
    public DiffStat Stat => File?.Stat ?? _folderStat;
    public bool IsStatVisible => IsFile || !IsExpanded;
    public List<ChangedFileTreeItemViewModel> Children { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpansionGlyph))]
    [NotifyPropertyChangedFor(nameof(IsStatVisible))]
    public partial bool IsExpanded { get; set; } = true;

    internal void UpdateFolderStat()
    {
        if (!IsFolder) { return; }
        DiffStat total = DiffStat.Sum(Children.ConvertAll(child => child.Stat));
        if (_folderStat.AddedLines == total.AddedLines && _folderStat.RemovedLines == total.RemovedLines) { return; }
        _folderStat = total;
        OnPropertyChanged(nameof(Stat));
    }

    [RelayCommand]
    private void OnToggleFolderExpansion()
    {
        if (IsFolder) { IsExpanded = !IsExpanded; }
    }
}
