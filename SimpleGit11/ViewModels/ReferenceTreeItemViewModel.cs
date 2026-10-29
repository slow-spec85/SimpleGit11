using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimpleGit11.Models;
using System;
using System.Collections.ObjectModel;
using System.Globalization;

namespace SimpleGit11.ViewModels;

// A presentation node owns its children and expansion state; Git references keep their full names.
public sealed partial class ReferenceTreeItemViewModel(
    string name,
    string fullName,
    ReferenceTreeItemViewModel? parent = null,
    GitBranch? branch = null,
    GitTag? tag = null) : ObservableObject
{
    public string Name { get; } = name;
    public string FullName { get; } = fullName;
    public ReferenceTreeItemViewModel? Parent { get; } = parent;
    public GitBranch? Branch { get; } = branch;
    public GitTag? Tag { get; } = tag;
    public bool IsFolder => Branch is null && Tag is null;
    public bool IsReference => !IsFolder;
    public ObservableCollection<ReferenceTreeItemViewModel> Children { get; } = [];
    internal (string Name, bool Folder, bool Remote, string RemoteName) Key =>
        (FullName, IsFolder, Branch?.IsRemote ?? Tag?.IsRemote ?? false, Tag?.RemoteName ?? "");
    internal string ExpansionStateKey { get; init; } = "";
    private DateTime? _latestDate = branch?.LastCommitDate ?? tag?.CreatedDate;
    public DateTime? LatestDate
    {
        get => _latestDate;
        internal set
        {
            if (SetProperty(ref _latestDate, value)) { OnPropertyChanged(nameof(Metadata)); }
        }
    }
    public string Metadata => IsFolder
        ? LatestDate?.ToString("g", CultureInfo.CurrentCulture) ?? ""
        : $"{Branch?.CommitMetadata ?? Tag?.CommitMetadata}   {Branch?.LastCommitMessage.Trim() ?? Tag?.Subject.Trim()}".Trim();
    public string CurrentIndicator => Branch?.CurrentIndicator ?? Tag?.CurrentIndicator ?? "";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [RelayCommand]
    private void OnToggleFolderExpansion()
    {
        if (IsFolder)
        {
            IsExpanded = !IsExpanded;
        }
    }
}
