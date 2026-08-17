using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SimpleGit11.Services;

public sealed class JsonLocalSettingsStore : ILocalSettingsStore
{
    private const string ApplicationDirectoryName = "SimpleGit11";
    private const string SettingsFileName = "settings.json";
    private readonly object _syncRoot = new();
    private readonly string _settingsFilePath;
    private readonly Dictionary<string, string> _values;

    public JsonLocalSettingsStore()
    {
        string localApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsFilePath = Path.Combine(localApplicationDataPath, ApplicationDirectoryName, SettingsFileName);
        _values = LoadValues();
    }

    public string? GetString(string key)
    {
        lock (_syncRoot)
        {
            return _values.TryGetValue(key, out string? value) ? value : null;
        }
    }

    public void SetString(string key, string value)
    {
        lock (_syncRoot)
        {
            _values[key] = value;
            SaveValues();
        }
    }

    private Dictionary<string, string> LoadValues()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return [];
            }

            string json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void SaveValues()
    {
        string? settingsDirectoryPath = Path.GetDirectoryName(_settingsFilePath);
        if (string.IsNullOrEmpty(settingsDirectoryPath))
        {
            throw new InvalidOperationException("The local settings directory could not be determined.");
        }

        Directory.CreateDirectory(settingsDirectoryPath);
        string temporaryFilePath = _settingsFilePath + ".tmp";

        try
        {
            string json = JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temporaryFilePath, json);
            File.Move(temporaryFilePath, _settingsFilePath, true);
        }
        finally
        {
            if (File.Exists(temporaryFilePath))
            {
                File.Delete(temporaryFilePath);
            }
        }
    }
}
