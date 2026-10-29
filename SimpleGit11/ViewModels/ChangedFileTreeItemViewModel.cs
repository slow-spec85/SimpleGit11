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
    public DiffStat Stat => File?.Stat ?? DiffStat.Empty;
    public List<ChangedFileTreeItemViewModel> Children { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpansionGlyph))]
    public partial bool IsExpanded { get; set; } = true;

    [RelayCommand]
    private void OnToggleFolderExpansion()
    {
        if (IsFolder) { IsExpanded = !IsExpanded; }
    }
}
