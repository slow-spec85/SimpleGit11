using System;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface ISettingsService
{
    event EventHandler? EditorAppearanceChanged;

    AppSettings Current { get; }

    void SetThemeMode(AppThemeMode themeMode);

    void SetLanguage(AppLanguage language);

    void SetIgnoreWhitespaceInDiff(bool ignoreWhitespace);

    void SetIncludePrereleaseVersions(bool includePrereleaseVersions);

    void SetEditorFont(string fontFamily, int fontSize);

    void SetEditorLineSpacing(int lineSpacing);

    void SetSshCommand(string sshCommand);
}
