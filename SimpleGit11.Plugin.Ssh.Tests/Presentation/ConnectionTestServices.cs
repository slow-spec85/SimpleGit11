using SimpleGit11.Plugin.Ssh.Models;
using SimpleGit11.Plugin.Ssh.Services;

namespace SimpleGit11.Plugin.Ssh.Tests.Presentation;

internal sealed class ConnectionTestProfiles : ISshConnectionProfileStore
{
    public List<SshConnectionProfile> Profiles { get; } = [];
    public IReadOnlyList<SshConnectionProfile> Load() => Profiles.ToArray();
    public void Upsert(SshConnectionProfile profile)
    {
        Delete(profile.Id);
        Profiles.Add(profile);
    }
    public void Delete(string profileId) => Profiles.RemoveAll(profile => profile.Id == profileId);
}

internal sealed class ConnectionTestLocalization : ISshLocalizationService
{
    public string GetString(string key) => key + " {0}";
}

internal sealed class ConnectionTestDialogs : ISshConnectionDialogService
{
    public Queue<SshConnectionDialogResult?> Results { get; } = new();
    public Queue<bool> Confirmations { get; } = new();
    public List<(string Title, string Message, string PrimaryButtonText)> Prompts { get; } = [];
    public List<(IReadOnlyList<SshConnectionProfile> Profiles, string? Selected)> Shown { get; } = [];
    public Func<Task<SshConnectionDialogResult?>>? Show { get; set; }
    public Func<Task<bool>>? Confirm { get; set; }
    public Task<SshConnectionDialogResult?> ShowSshConnectionDialogAsync(
        IReadOnlyList<SshConnectionProfile> profiles, string? selectedProfileId = null)
    {
        Shown.Add((profiles, selectedProfileId));
        return Show?.Invoke() ?? Task.FromResult(Results.Dequeue());
    }

    public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText)
    {
        Prompts.Add((title, message, primaryButtonText));
        return Confirm?.Invoke() ?? Task.FromResult(Confirmations.Dequeue());
    }
}
