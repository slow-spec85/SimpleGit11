using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SimpleGit11.Models;
using SimpleGit11.Services;
using System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace SimpleGit11.Presentation.Theming;

internal static class ThemeResourceResolver
{
    private static readonly AccessibilitySettings AccessibilitySettings = new();
    private static readonly UISettings UISettings = new();

    public static bool IsHighContrast => AccessibilitySettings.HighContrast;

    public static Brush? GetDiffLineBackgroundBrush(DiffLineKind kind)
    {
        string? resourceKey = kind switch
        {
            DiffLineKind.Added => "DiffAddedLineBackgroundBrush",
            DiffLineKind.Removed => "DiffRemovedLineBackgroundBrush",
            DiffLineKind.Hunk or DiffLineKind.Header => "DiffHunkLineBackgroundBrush",
            DiffLineKind.ConflictMarker => "DiffConflictMarkerLineBackgroundBrush",
            _ => null
        };

        return resourceKey is null ? null : GetBrush(resourceKey);
    }

    public static Brush? GetDiffLineAccentBrush(DiffLineKind kind)
    {
        string resourceKey = kind switch
        {
            DiffLineKind.Hunk or DiffLineKind.Header => "DiffHunkLineAccentBrush",
            DiffLineKind.ConflictMarker => "DiffConflictMarkerLineAccentBrush",
            _ => "DefaultLineAccentBrush"
        };

        return GetBrush(resourceKey);
    }

    public static Color GetColor(string resourceKey)
    {
        if (GetCurrentThemeDictionary()?[resourceKey] is SolidColorBrush brush)
        {
            return brush.Color;
        }

        throw new InvalidOperationException($"Theme resource '{resourceKey}' must be a SolidColorBrush.");
    }

    public static Color GetThemeColor(string themeKey, string resourceKey)
    {
        if (Application.Current.Resources.ThemeDictionaries[themeKey] is ResourceDictionary dictionary
            && dictionary[resourceKey] is SolidColorBrush brush)
        {
            return brush.Color;
        }

        throw new InvalidOperationException(
            $"Theme resource '{resourceKey}' must be a SolidColorBrush in theme '{themeKey}'.");
    }

    public static Brush GetBrush(string resourceKey)
    {
        if (GetCurrentThemeDictionary()?[resourceKey] is Brush brush)
        {
            return brush;
        }

        throw new InvalidOperationException($"Theme resource '{resourceKey}' must be a Brush.");
    }

    private static ResourceDictionary? GetCurrentThemeDictionary()
    {
        string themeKey = AccessibilitySettings.HighContrast
            ? "HighContrast"
            : App.GetService<IThemeService>().CurrentTheme switch
            {
                AppThemeMode.Dark => "Dark",
                AppThemeMode.Light => "Light",
                _ => IsSystemDarkTheme() ? "Dark" : "Light"
            };

        return Application.Current.Resources.ThemeDictionaries[themeKey] as ResourceDictionary;
    }

    private static bool IsSystemDarkTheme()
    {
        var background = UISettings.GetColorValue(UIColorType.Background);
        return background.R < 128 && background.G < 128 && background.B < 128;
    }
}
