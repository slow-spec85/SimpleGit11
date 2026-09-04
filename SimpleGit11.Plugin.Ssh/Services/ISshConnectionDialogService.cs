using SimpleGit11.Plugin.Ssh.Models;

namespace SimpleGit11.Plugin.Ssh.Services;

internal interface ISshConnectionDialogService
{
    Task<SshConnectionDialogResult?> ShowSshConnectionDialogAsync(
        IReadOnlyList<SshConnectionProfile> profiles, string? selectedProfileId = null);
    Task<bool> ConfirmAsync(string title, string message, string primaryButtonText);
}
