using System;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public sealed class SettingsService : ISettingsService
{
    private const int MinimumEditorFontSize = 8;
    private const int MaximumEditorFontSize = 32;
    private const int MinimumEditorLineSpacing = 0;
    private const int MaximumEditorLineSpacing = 16;
    private const string ThemeModeKey = "ThemeMode";
    private const string LanguageKey = "Language";
    private const string IgnoreWhitespaceInDiffKey = "IgnoreWhitespaceInDiff";
    private const string IncludePrereleaseVersionsKey = "IncludePrereleaseVersions";
    private const string EditorFontFamilyKey = "EditorFontFamily";
    private const string EditorFontSizeKey = "EditorFontSize";
    private const string EditorLineSpacingKey = "EditorLineSpacing";
    private readonly ILocalSettingsStore _localSettingsStore;

    public SettingsService(
        ILocalSettingsStore localSettingsStore,
        IProductInfoService productInfoService)
    {
        _localSettingsStore = localSettingsStore;
        bool currentVersionIsPrerelease = productInfoService.CurrentVersion.Contains(
            '-',
            StringComparison.Ordinal);
        Current = new AppSettings
        {
            ThemeMode = LoadEnum(ThemeModeKey, AppThemeMode.System),
            Language = LoadEnum(LanguageKey, AppLanguage.System),
            IgnoreWhitespaceInDiff = LoadBool(IgnoreWhitespaceInDiffKey, false),
            IncludePrereleaseVersions = LoadBool(
                IncludePrereleaseVersionsKey,
                currentVersionIsPrerelease),
            EditorFontFamily = LoadString(
                EditorFontFamilyKey,
                AppSettings.DefaultEditorFontFamily),
            EditorFontSize = LoadInt(
                EditorFontSizeKey,
                AppSettings.DefaultEditorFontSize,
                MinimumEditorFontSize,
                MaximumEditorFontSize),
            EditorLineSpacing = LoadInt(
                EditorLineSpacingKey,
                AppSettings.DefaultEditorLineSpacing,
                MinimumEditorLineSpacing,
                MaximumEditorLineSpacing)
        };
    }

    public event EventHandler? EditorAppearanceChanged;

    public AppSettings Current { get; }

    public void SetThemeMode(AppThemeMode themeMode)
    {
        Current.ThemeMode = themeMode;
        SaveEnum(ThemeModeKey, themeMode);
    }

    public void SetLanguage(AppLanguage language)
    {
        Current.Language = language;
        SaveEnum(LanguageKey, language);
    }

    public void SetIgnoreWhitespaceInDiff(bool ignoreWhitespace)
    {
        Current.IgnoreWhitespaceInDiff = ignoreWhitespace;
        _localSettingsStore.SetString(IgnoreWhitespaceInDiffKey, ignoreWhitespace.ToString());
    }

    public void SetIncludePrereleaseVersions(bool includePrereleaseVersions)
    {
        Current.IncludePrereleaseVersions = includePrereleaseVersions;
        _localSettingsStore.SetString(
            IncludePrereleaseVersionsKey,
            includePrereleaseVersions.ToString());
    }

    public void SetEditorFont(string fontFamily, int fontSize)
    {
        string normalizedFamily = string.IsNullOrWhiteSpace(fontFamily)
            ? AppSettings.DefaultEditorFontFamily
            : fontFamily.Trim();
        int normalizedSize = Math.Clamp(fontSize, MinimumEditorFontSize, MaximumEditorFontSize);
        if (string.Equals(Current.EditorFontFamily, normalizedFamily, StringComparison.Ordinal)
            && Current.EditorFontSize == normalizedSize)
        {
            return;
        }

        Current.EditorFontFamily = normalizedFamily;
        Current.EditorFontSize = normalizedSize;
        _localSettingsStore.SetString(EditorFontFamilyKey, normalizedFamily);
        _localSettingsStore.SetString(EditorFontSizeKey, normalizedSize.ToString());
        EditorAppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetEditorLineSpacing(int lineSpacing)
    {
        int normalizedSpacing = Math.Clamp(
            lineSpacing,
            MinimumEditorLineSpacing,
            MaximumEditorLineSpacing);
        if (Current.EditorLineSpacing == normalizedSpacing)
        {
            return;
        }

        Current.EditorLineSpacing = normalizedSpacing;
        _localSettingsStore.SetString(EditorLineSpacingKey, normalizedSpacing.ToString());
        EditorAppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private T LoadEnum<T>(string key, T fallback)
        where T : struct, Enum
    {
        string? value = _localSettingsStore.GetString(key);
        return Enum.TryParse(value, out T result) ? result : fallback;
    }

    private void SaveEnum<T>(string key, T value)
        where T : struct, Enum
    {
        _localSettingsStore.SetString(key, value.ToString());
    }

    private bool LoadBool(string key, bool fallback)
    {
        return bool.TryParse(_localSettingsStore.GetString(key), out bool value)
            ? value
            : fallback;
    }

    private string LoadString(string key, string fallback)
    {
        string? value = _localSettingsStore.GetString(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private int LoadInt(string key, int fallback, int minimum, int maximum)
    {
        return int.TryParse(_localSettingsStore.GetString(key), out int value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
    }
}
