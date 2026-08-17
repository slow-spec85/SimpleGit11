using CommunityToolkit.Mvvm.ComponentModel;
using SimpleGit11.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SimpleGit11.ViewModels;

public sealed partial class ArchiveDialogViewModel : ViewModelBase, IDisposable
{
    public ArchiveDialogViewModel(
        GitRevisionSelectorViewModel revisionSelector)
    {
        RevisionSelector = revisionSelector;
        RevisionSelector.PropertyChanged += OnRevisionSelectorPropertyChanged;

        FormatOptions.Add(new DisplayOption<GitArchiveFormat>(GitArchiveFormat.Zip, "ZIP"));
        FormatOptions.Add(new DisplayOption<GitArchiveFormat>(GitArchiveFormat.TarGZip, "TAR.GZ"));
        FormatOptions.Add(new DisplayOption<GitArchiveFormat>(GitArchiveFormat.Tar, "TAR"));
        SelectedFormatOption = FormatOptions[0];
        IncludeRootDirectory = true;
    }

    public GitRevisionSelectorViewModel RevisionSelector { get; }

    public ObservableCollection<DisplayOption<GitArchiveFormat>> FormatOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanArchive))]
    public partial DisplayOption<GitArchiveFormat> SelectedFormatOption { get; set; }

    [ObservableProperty]
    public partial bool IncludeRootDirectory { get; set; }

    public bool CanArchive => RevisionSelector.CanResolve;

    public Task<bool> ResolveStartPointAsync()
    {
        return RevisionSelector.ResolveAsync();
    }

    public GitArchiveDialogResult CreateResult()
    {
        return new GitArchiveDialogResult(
            RevisionSelector.StartPoint.Trim(),
            RevisionSelector.ResolvedRevision!.CommitHash,
            SelectedFormatOption.Value,
            IncludeRootDirectory);
    }

    public void Dispose()
    {
        RevisionSelector.PropertyChanged -= OnRevisionSelectorPropertyChanged;
        RevisionSelector.Dispose();
    }

    private void OnRevisionSelectorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GitRevisionSelectorViewModel.CanResolve)
            or nameof(GitRevisionSelectorViewModel.IsLoading)
            or nameof(GitRevisionSelectorViewModel.IsValidating)
            or nameof(GitRevisionSelectorViewModel.StartPoint))
        {
            OnPropertyChanged(nameof(CanArchive));
        }
    }
}
