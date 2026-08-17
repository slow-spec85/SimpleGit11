using System;
using System.Globalization;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public static class PluralizationService
{
    public static string FormatChangeCount(int count, ILocalizationService localizationService)
    {
        string resourceKey = UsesRussianPluralRules(localizationService)
            ? GetRussianChangeCountResourceKey(count)
            : GetEnglishChangeCountResourceKey(count);

        return string.Format(
            CultureInfo.CurrentCulture,
            "{0} {1}",
            count,
            localizationService.GetString(resourceKey));
    }

    public static string FormatCommitCount(int count, ILocalizationService localizationService)
    {
        string resourceKey = UsesRussianPluralRules(localizationService)
            ? GetRussianCommitCountResourceKey(count)
            : GetEnglishCommitCountResourceKey(count);

        return string.Format(
            CultureInfo.CurrentCulture,
            "{0} {1}",
            count,
            localizationService.GetString(resourceKey));
    }

    private static bool UsesRussianPluralRules(ILocalizationService localizationService)
    {
        if (localizationService.CurrentLanguage == AppLanguage.Russian)
        {
            return true;
        }

        return localizationService.CurrentLanguage == AppLanguage.System
            && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRussianChangeCountResourceKey(int count)
    {
        int absoluteCount = Math.Abs(count);
        int lastTwoDigits = absoluteCount % 100;
        int lastDigit = absoluteCount % 10;

        if (lastTwoDigits is >= 11 and <= 14)
        {
            return "ChangeCountMany";
        }

        return lastDigit switch
        {
            1 => "ChangeCountOne",
            >= 2 and <= 4 => "ChangeCountFew",
            _ => "ChangeCountMany"
        };
    }

    private static string GetEnglishChangeCountResourceKey(int count)
    {
        return Math.Abs(count) == 1
            ? "ChangeCountOne"
            : "ChangeCountMany";
    }

    private static string GetRussianCommitCountResourceKey(int count)
    {
        int absoluteCount = Math.Abs(count);
        int lastTwoDigits = absoluteCount % 100;
        int lastDigit = absoluteCount % 10;

        if (lastTwoDigits is >= 11 and <= 14)
        {
            return "CommitCountMany";
        }

        return lastDigit switch
        {
            1 => "CommitCountOne",
            >= 2 and <= 4 => "CommitCountFew",
            _ => "CommitCountMany"
        };
    }

    private static string GetEnglishCommitCountResourceKey(int count)
    {
        return Math.Abs(count) == 1
            ? "CommitCountOne"
            : "CommitCountMany";
    }
}
