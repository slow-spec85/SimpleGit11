using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SimpleGit11.Services.Execution;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Plugin.Ssh.Services;

public static class RemoteCommandComposer
{
    public static string ComposeGit(
        RepositoryPathStyle style,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environmentVariables,
        bool useDefaultWorkingDirectory = false,
        GitHttpAuthentication? httpAuthentication = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);
        return style == RepositoryPathStyle.Windows
            ? ComposeWindowsGit(workingDirectory, arguments, environmentVariables, useDefaultWorkingDirectory)
            : ComposePosixGit(
                workingDirectory,
                arguments,
                environmentVariables,
                useDefaultWorkingDirectory,
                httpAuthentication);
    }

    private static string ComposePosixGit(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environmentVariables,
        bool useDefaultWorkingDirectory,
        GitHttpAuthentication? httpAuthentication)
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

        bool hasCredential = httpAuthentication is
        {
            Username: not null,
            Password: not null
        };
        if (httpAuthentication is not null)
        {
            command.Append("GIT_TERMINAL_PROMPT='0' ");
        }

        if (hasCredential)
        {
            command.Append("sh -c ")
                .Append(QuotePosix("exec 3<&0; exec \"$@\""))
                .Append(" sh ");
        }

        command.Append("git");
        if (httpAuthentication is not null)
        {
            command.Append(" -c ").Append(QuotePosix("credential.helper="));
            if (hasCredential)
            {
                command.Append(" -c ")
                    .Append(QuotePosix($"credential.helper={CreateCredentialHelper(httpAuthentication.Url)}"));
            }
        }

        foreach (string argument in arguments)
        {
            command.Append(' ').Append(QuotePosix(argument));
        }

        return command.ToString();
    }

    private static string CreateCredentialHelper(string remoteUrl)
    {
        Uri uri = new(remoteUrl, UriKind.Absolute);
        string host = uri.IsDefaultPort ? uri.IdnHost : uri.Authority;
        string helper = "!f() { "
            + "[ \"$1\" = get ] || exit 0; "
            + "protocol=; host=; "
            + "while IFS= read -r line; do "
            + "[ -z \"$line\" ] && break; "
            + "case \"$line\" in "
            + "protocol=*) protocol=${line#protocol=} ;; "
            + "host=*) host=${line#host=} ;; "
            + "esac; done; "
            + $"[ \"$protocol\" = {QuotePosix(uri.Scheme)} ] "
            + $"&& [ \"$host\" = {QuotePosix(host)} ] "
            + "&& cat <&3; }; f";
        return helper;
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
