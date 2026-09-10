using System;
using System.Collections.Generic;

namespace SimpleGit11.Presentation.Services;

internal static class ApplicationLaunchArguments
{
    internal const string OpenRepositoryOption = "--open-repository";

    public static string? GetRepositoryPath(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        for (int index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], OpenRepositoryOption, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }
}
