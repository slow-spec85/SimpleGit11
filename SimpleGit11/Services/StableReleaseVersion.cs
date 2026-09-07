using System;

namespace SimpleGit11.Services;

internal static class StableReleaseVersion
{
    public static bool IsStable(string version)
    {
        return TryParse(version, allowPrerelease: false, out _);
    }

    public static bool IsNewer(string currentVersion, string releaseVersion)
    {
        return TryParse(currentVersion, allowPrerelease: true, out ParsedVersion current)
            && TryParse(releaseVersion, allowPrerelease: false, out ParsedVersion release)
            && release.CompareTo(current) > 0;
    }

    private static bool TryParse(
        string value,
        bool allowPrerelease,
        out ParsedVersion version)
    {
        version = default;
        string normalized = value.Split('+', 2)[0].Trim();
        int prereleaseSeparator = normalized.IndexOf('-');
        bool isPrerelease = prereleaseSeparator >= 0;
        if (isPrerelease)
        {
            if (!allowPrerelease || prereleaseSeparator == normalized.Length - 1)
            {
                return false;
            }

            normalized = normalized[..prereleaseSeparator];
        }

        string[] parts = normalized.Split('.');
        if (parts.Length != 3
            || !TryParseComponent(parts[0], out int major)
            || !TryParseComponent(parts[1], out int minor)
            || !TryParseComponent(parts[2], out int patch))
        {
            return false;
        }

        version = new ParsedVersion(major, minor, patch, isPrerelease);
        return true;
    }

    private static bool TryParseComponent(string value, out int component)
    {
        return int.TryParse(value, out component)
            && component >= 0
            && (value.Length == 1 || value[0] != '0');
    }

    private readonly record struct ParsedVersion(
        int Major,
        int Minor,
        int Patch,
        bool IsPrerelease) : IComparable<ParsedVersion>
    {
        public int CompareTo(ParsedVersion other)
        {
            int comparison = Major.CompareTo(other.Major);
            if (comparison == 0)
            {
                comparison = Minor.CompareTo(other.Minor);
            }

            if (comparison == 0)
            {
                comparison = Patch.CompareTo(other.Patch);
            }

            if (comparison == 0)
            {
                comparison = other.IsPrerelease.CompareTo(IsPrerelease);
            }

            return comparison;
        }
    }
}
