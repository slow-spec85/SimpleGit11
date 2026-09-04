using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Plugin.Ssh.Services;

public static class RemoteCommandComposer
{
    public static string ComposeGit(
        RepositoryPathStyle style,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environmentVariables,
        bool useDefaultWorkingDirectory = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);
        return style == RepositoryPathStyle.Windows
            ? ComposeWindowsGit(workingDirectory, arguments, environmentVariables, useDefaultWorkingDirectory)
            : ComposePosixGit(workingDirectory, arguments, environmentVariables, useDefaultWorkingDirectory);
    }

    private static string ComposePosixGit(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environmentVariables,
        bool useDefaultWorkingDirectory)
    {
        StringBuilder command = new();
        if (!useDefaultWorkingDirectory)
        {
            command.Append("cd -- ").Append(QuotePosix(workingDirectory)).Append(" && ");
        }
        if (environmentVariables is not null)
        {
            foreach ((string name, string value) in environmentVariables)
            {
                ValidateEnvironmentVariableName(name);
                command.Append(name).Append('=').Append(QuotePosix(value)).Append(' ');
            }
        }

        command.Append("git");
        foreach (string argument in arguments)
        {
            command.Append(' ').Append(QuotePosix(argument));
        }

        return command.ToString();
    }

    private static string ComposeWindowsGit(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environmentVariables,
        bool useDefaultWorkingDirectory)
    {
        StringBuilder script = new();
        script.Append("$ErrorActionPreference='Stop';");
        if (!useDefaultWorkingDirectory)
        {
            script.Append(" Set-Location -LiteralPath ")
                .Append(QuotePowerShell(workingDirectory)).Append(';');
        }
        if (environmentVariables is not null)
        {
            foreach ((string name, string value) in environmentVariables)
            {
                ValidateEnvironmentVariableName(name);
                script.Append("$env:").Append(name).Append('=')
                    .Append(QuotePowerShell(value)).Append(';');
            }
        }

        script.Append("& git");
        foreach (string argument in arguments)
        {
            script.Append(' ').Append(QuotePowerShell(argument));
        }

        script.Append("; exit $LASTEXITCODE");
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ToString()));
        return $"powershell.exe -NoLogo -NoProfile -NonInteractive -EncodedCommand {encoded}";
    }

    private static string QuotePosix(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
    }

    private static string QuotePowerShell(string value)
    {
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    private static void ValidateEnvironmentVariableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            !name.All(character => character == '_' || char.IsLetterOrDigit(character)) ||
            char.IsDigit(name[0]))
        {
            throw new ArgumentException($"'{name}' is not a valid environment variable name.");
        }
    }
}
