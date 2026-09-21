using System;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface ISettingsService
{
    event EventHandler? EditorAppearanceChanged;

    AppSettings Current { get; }

    void SetThemeMode(AppThemeMode themeMode);

    void SetLanguage(AppLanguage language);

    void SetDefaultRemoteName(string remoteName);

    void SetFetchOnRepositoryOpen(bool fetch);

    void SetRecentRepositoriesCount(int count);

    void SetOpenLastRepositoryOnStartup(bool open);

    void SetIgnoreWhitespaceInDiff(bool ignoreWhitespace);

    void SetEditorFont(string fontFamily, int fontSize);

    void SetEditorLineSpacing(int lineSpacing);
}
