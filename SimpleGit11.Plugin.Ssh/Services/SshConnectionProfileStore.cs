using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SimpleGit11.Services;

namespace SimpleGit11.Plugin.Ssh.Services;

public sealed class SshConnectionProfileStore : ISshConnectionProfileStore
{
    private const string SettingsKey = "SshConnectionProfiles";
    private readonly ILocalSettingsStore _settingsStore;

    public SshConnectionProfileStore(ILocalSettingsStore settingsStore) => _settingsStore = settingsStore;

    public IReadOnlyList<SshConnectionProfile> Load()
    {
        string? json = _settingsStore.GetString(SettingsKey);
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<List<SshConnectionProfile>>(json) ?? [])
                .OrderByDescending(profile => profile.LastUsedAt)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void Upsert(SshConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        List<SshConnectionProfile> profiles = Load()
            .Where(item => !string.Equals(item.Id, profile.Id, StringComparison.Ordinal))
            .ToList();
        profiles.Add(profile);
        _settingsStore.SetString(SettingsKey, JsonSerializer.Serialize(profiles));
    }

    public void Delete(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        SshConnectionProfile[] profiles = Load()
            .Where(profile => !string.Equals(profile.Id, profileId, StringComparison.Ordinal))
            .ToArray();
        _settingsStore.SetString(SettingsKey, JsonSerializer.Serialize(profiles));
    }
}
