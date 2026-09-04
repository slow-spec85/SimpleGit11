using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.Plugin.Ssh.Services;

// Embedded resources keep plugin installation independent of the host's compiled PRI.
internal sealed class SshLocalizationService(ILocalizationService hostLocalization) : ISshLocalizationService
{
    private static readonly IReadOnlyDictionary<string, string> English = Load("en-US");
    private static readonly IReadOnlyDictionary<string, string> Russian = Load("ru-RU");

    public string GetString(string key)
    {
        bool russian = hostLocalization.CurrentLanguage == AppLanguage.Russian
            || (hostLocalization.CurrentLanguage == AppLanguage.System
                && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru");
        IReadOnlyDictionary<string, string> resources = russian ? Russian : English;
        return resources.TryGetValue(key, out string? value) ? value
            : throw new KeyNotFoundException($"Missing SSH resource: {key}");
    }

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        using Stream stream = typeof(SshLocalizationService).Assembly.GetManifestResourceStream(
            $"Ssh.Strings.{language}.Resources.resw")
            ?? throw new InvalidOperationException($"Missing SSH resources: {language}");
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        return XDocument.Load(reader).Root!.Elements("data").ToDictionary(
            element => (string)element.Attribute("name")!,
            element => element.Element("value")!.Value.Replace("\\n", "\n"),
            StringComparer.Ordinal);
    }
}
