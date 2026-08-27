using CommunityToolkit.Mvvm.ComponentModel;
using SimpleGit11.Models;

namespace SimpleGit11.ViewModels;

public sealed partial class SubmoduleAddDialogViewModel : ObservableObject
{
    public SubmoduleAddDialogViewModel(string defaultPath)
    {
        Url = "";
        Path = defaultPath;
        Branch = "";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    public partial string Url { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    public partial string Path { get; set; }

    [ObservableProperty]
    public partial string Branch { get; set; }

    public bool CanAdd => !string.IsNullOrWhiteSpace(Url)
        && !string.IsNullOrWhiteSpace(Path);

    public SubmoduleAddRequest CreateRequest()
    {
        return new SubmoduleAddRequest(Url.Trim(), Path.Trim(), Branch.Trim());
    }
}
