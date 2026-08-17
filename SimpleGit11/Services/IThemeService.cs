using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IThemeService
{
    AppThemeMode CurrentTheme { get; }

    void SetTheme(AppThemeMode themeMode);
}
