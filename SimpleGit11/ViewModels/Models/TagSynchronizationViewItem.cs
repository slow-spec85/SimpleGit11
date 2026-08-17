using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.ViewModels;

public sealed class TagSynchronizationViewItem
{
    public TagSynchronizationViewItem(
        TagSynchronizationItem tag,
        GitRemote remote,
        ILocalizationService localizationService)
    {
        Tag = tag;
        Name = tag.Name;
        Description = tag.HasConflict
            ? localizationService.GetString("SynchronizationTagConflictDescription")
            : string.Format(
                localizationService.GetString("SynchronizationTagUnpublishedDescription"),
                remote.Name);
    }

    public TagSynchronizationItem Tag { get; }

    public string Name { get; }

    public string Description { get; }

    public bool CanPush => Tag.CanPush;

    public bool HasConflict => Tag.HasConflict;
}
