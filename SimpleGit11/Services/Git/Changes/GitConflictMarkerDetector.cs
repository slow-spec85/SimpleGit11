using System;
using System.Linq;

namespace SimpleGit11.Services;

internal static class GitConflictMarkerDetector
{
    public static bool ContainsMarkers(string content)
    {
        string normalizedContent = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        return normalizedContent.Split('\n').Any(IsMarker);
    }

    public static bool IsMarker(string line)
    {
        return line.StartsWith("<<<<<<<", StringComparison.Ordinal)
            || line.StartsWith("|||||||", StringComparison.Ordinal)
            || line.StartsWith("=======", StringComparison.Ordinal)
            || line.StartsWith(">>>>>>>", StringComparison.Ordinal);
    }
}
