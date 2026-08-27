using CommunityToolkit.Mvvm.ComponentModel;
using SimpleGit11.Models;

namespace SimpleGit11.ViewModels;

public sealed partial class GitUrlRewriteDialogViewModel : ObservableObject
{
    public GitUrlRewriteDialogViewModel(GitUrlRewrite? rewrite)
    {
        InsteadOfUrl = rewrite?.InsteadOfUrl ?? "";
        ReplacementUrl = rewrite?.ReplacementUrl ?? "";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string InsteadOfUrl { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string ReplacementUrl { get; set; }

    public bool CanSave => !string.IsNullOrWhiteSpace(InsteadOfUrl)
        && !string.IsNullOrWhiteSpace(ReplacementUrl);

    public GitUrlRewrite CreateRewrite()
    {
        return new GitUrlRewrite(InsteadOfUrl.Trim(), ReplacementUrl.Trim());
    }
}
