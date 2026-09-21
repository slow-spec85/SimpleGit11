namespace SimpleGit11.Models;

public sealed class AppSettings
{
    public const string DefaultEditorFontFamily = "Consolas";
    public const int DefaultEditorFontSize = 14;
    public const int DefaultEditorLineSpacing = 2;
    public const int DefaultRecentRepositoriesCount = 8;

    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;

    public AppLanguage Language { get; set; } = AppLanguage.System;

    public string DefaultRemoteName { get; set; } = "origin";

    public bool FetchOnRepositoryOpen { get; set; }

    public int RecentRepositoriesCount { get; set; } = DefaultRecentRepositoriesCount;

    public bool OpenLastRepositoryOnStartup { get; set; }

    public bool IgnoreWhitespaceInDiff { get; set; }

    public string EditorFontFamily { get; set; } = DefaultEditorFontFamily;

    public int EditorFontSize { get; set; } = DefaultEditorFontSize;

    public int EditorLineSpacing { get; set; } = DefaultEditorLineSpacing;
}
