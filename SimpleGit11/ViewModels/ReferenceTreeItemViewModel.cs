using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimpleGit11.Models;
using System;
using System.Collections.Generic;
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
    public List<ReferenceTreeItemViewModel> Children { get; } = [];
    public DateTime? LatestDate { get; internal set; } = branch?.LastCommitDate ?? tag?.CreatedDate;
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
