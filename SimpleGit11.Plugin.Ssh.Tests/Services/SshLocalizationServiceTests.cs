using System.Globalization;
using System.Xml.Linq;
using SimpleGit11.Models;
using SimpleGit11.Plugin.Ssh.Services;
using SimpleGit11.Services;

namespace SimpleGit11.Plugin.Ssh.Tests.Services;

[TestClass]
public sealed class SshLocalizationServiceTests
{
    [TestMethod]
    [DataRow(AppLanguage.English, "SSH connection")]
    [DataRow(AppLanguage.Russian, "Подключение по SSH")]
    public void GetString_UsesHostLanguageWithoutHostSshResources(AppLanguage language, string expected)
    {
        SshLocalizationService localization = new(new HostLocalization(language));
        Assert.AreEqual(expected, localization.GetString("SshConnectionNavigationItem"));
        Assert.IsFalse(localization.GetString("SshHostKeyDialogMessage").Contains("\\n", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("ru-RU", "Подключение по SSH")]
    [DataRow("de-DE", "SSH connection")]
    public void GetString_SystemLanguage_UsesRussianOrEnglishFallback(string culture, string expected)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            SshLocalizationService localization = new(new HostLocalization(AppLanguage.System));
            Assert.AreEqual(expected, localization.GetString("SshConnectionNavigationItem"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [TestMethod]
    [DataRow(AppLanguage.English, "Disconnect SSH?", "Disconnect from server and return to working on the local machine?", "Disconnect")]
    [DataRow(AppLanguage.Russian, "Отключиться от SSH?", "Отключиться от server и вернуться к работе на локальной машине?", "Отключиться")]
    public void GetString_DisconnectConfirmation_IsLocalized(AppLanguage language, string title, string message, string button)
    {
        SshLocalizationService localization = new(new HostLocalization(language));

        Assert.AreEqual(title, localization.GetString("SshDisconnectDialogTitle"));
        Assert.AreEqual(message, string.Format(localization.GetString("SshDisconnectDialogMessage"), "server"));
        Assert.AreEqual(button, localization.GetString("SshDisconnectConfirmButton"));
    }

    [TestMethod]
    public void Resources_BothLanguagesHaveTheSameNonEmptyKeys()
    {
        string[]? englishKeys = null;
        foreach (string language in new[] { "en-US", "ru-RU" })
        {
            using Stream stream = typeof(SshPlugin).Assembly.GetManifestResourceStream($"Ssh.Strings.{language}.Resources.resw")!;
            XElement[] entries = XDocument.Load(stream).Root!.Elements("data").ToArray();
            Assert.IsTrue(entries.All(entry => !string.IsNullOrWhiteSpace(entry.Element("value")?.Value)));
            string[] keys = entries.Select(entry => (string)entry.Attribute("name")!).ToArray();
            Assert.AreEqual(keys.Length, keys.Distinct().Count());
            if (englishKeys is null) englishKeys = keys;
            else CollectionAssert.AreEquivalent(englishKeys, keys);
        }
    }

    private sealed class HostLocalization(AppLanguage language) : ILocalizationService
    {
        public AppLanguage CurrentLanguage => language;
        public string GetString(string key) => throw new InvalidOperationException("Host SSH resources must not be used.");
        public void ApplyLanguage() { }
        public void SetLanguage(AppLanguage value) { }
    }
}
