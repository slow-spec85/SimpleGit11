using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface ILocalizationService
{
    AppLanguage CurrentLanguage { get; }

    string GetString(string resourceKey);

    void ApplyLanguage();

    void SetLanguage(AppLanguage language);
}
