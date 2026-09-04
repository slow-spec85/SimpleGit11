namespace SimpleGit11.Models;

public sealed class AppSettings
{
    public const string DefaultEditorFontFamily = "Consolas";
    public const int DefaultEditorFontSize = 14;
    public const int DefaultEditorLineSpacing = 2;

    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;

    public AppLanguage Language { get; set; } = AppLanguage.System;

    public bool IgnoreWhitespaceInDiff { get; set; }

    public bool IncludePrereleaseVersions { get; set; }

    public string EditorFontFamily { get; set; } = DefaultEditorFontFamily;

    public int EditorFontSize { get; set; } = DefaultEditorFontSize;

    public int EditorLineSpacing { get; set; } = DefaultEditorLineSpacing;
}
