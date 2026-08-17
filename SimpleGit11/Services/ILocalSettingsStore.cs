namespace SimpleGit11.Services;

public interface ILocalSettingsStore
{
    string? GetString(string key);

    void SetString(string key, string value);
}
