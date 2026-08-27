using System;
using System.Collections.Generic;

namespace SimpleGit11.Services;

internal static class GitSubmoduleConfigurationParser
{
    private const string Prefix = "submodule.";

    public static IReadOnlyList<GitSubmoduleConfiguration> Parse(string output)
    {
        Dictionary<string, MutableConfiguration> configurations = new(StringComparer.Ordinal);
        List<string> names = [];

        foreach (string record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int separatorIndex = record.IndexOf('\n');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = record[..separatorIndex];
            string value = record[(separatorIndex + 1)..];
            if (!key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string subsectionAndProperty = key[Prefix.Length..];
            int propertySeparatorIndex = subsectionAndProperty.LastIndexOf('.');
            if (propertySeparatorIndex <= 0 || propertySeparatorIndex == subsectionAndProperty.Length - 1)
            {
                continue;
            }

            string name = subsectionAndProperty[..propertySeparatorIndex];
            string property = subsectionAndProperty[(propertySeparatorIndex + 1)..];
            if (!configurations.TryGetValue(name, out MutableConfiguration? configuration))
            {
                configuration = new MutableConfiguration(name);
                configurations.Add(name, configuration);
                names.Add(name);
            }

            if (property.Equals("path", StringComparison.OrdinalIgnoreCase))
            {
                configuration.Path = value;
            }
            else if (property.Equals("url", StringComparison.OrdinalIgnoreCase))
            {
                configuration.Url = value;
            }
            else if (property.Equals("branch", StringComparison.OrdinalIgnoreCase))
            {
                configuration.Branch = value;
            }
        }

        List<GitSubmoduleConfiguration> result = [];
        foreach (string name in names)
        {
            MutableConfiguration configuration = configurations[name];
            if (!string.IsNullOrWhiteSpace(configuration.Path))
            {
                result.Add(new GitSubmoduleConfiguration(
                    configuration.Name,
                    configuration.Path,
                    configuration.Url,
                    configuration.Branch));
            }
        }

        return result;
    }

    private sealed class MutableConfiguration(string name)
    {
        public string Name { get; } = name;

        public string Path { get; set; } = "";

        public string Url { get; set; } = "";

        public string Branch { get; set; } = "";
    }
}

internal sealed record GitSubmoduleConfiguration(
    string Name,
    string Path,
    string Url,
    string Branch);
