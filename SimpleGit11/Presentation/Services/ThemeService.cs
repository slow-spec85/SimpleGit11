using Microsoft.UI.Xaml;
using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.Presentation.Services;

public sealed class ThemeService : IThemeService
{
    private readonly ISettingsService _settingsService;
    private Window? _window;

    public ThemeService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public AppThemeMode CurrentTheme => _settingsService.Current.ThemeMode;

    public void RegisterWindow(Window window)
    {
        _window = window;
        ApplyTheme();
    }

    public void SetTheme(AppThemeMode themeMode)
    {
        _settingsService.SetThemeMode(themeMode);
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (_window?.Content is not FrameworkElement rootElement)
        {
            return;
        }

        rootElement.RequestedTheme = CurrentTheme switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }
}
