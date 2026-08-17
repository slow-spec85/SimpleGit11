using SimpleGit11.Models;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;

namespace SimpleGit11.Services;

public sealed class LocalizationService : ILocalizationService
{
    private readonly ISettingsService _settingsService;

    public LocalizationService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public AppLanguage CurrentLanguage => _settingsService.Current.Language;

    public void ApplyLanguage()
    {
        string? languageTag = CurrentLanguage switch
        {
            AppLanguage.English => "en-US",
            AppLanguage.Russian => "ru-RU",
            _ => null
        };

        if (languageTag is not null)
        {
            ApplicationLanguages.PrimaryLanguageOverride = languageTag;
        }
    }

    public string GetString(string resourceKey)
    {
        var value = new ResourceLoader().GetString(resourceKey);
        return string.IsNullOrEmpty(value) ? resourceKey : value;
    }

    public void SetLanguage(AppLanguage language)
    {
        _settingsService.SetLanguage(language);
        ApplyLanguage();
    }
}
