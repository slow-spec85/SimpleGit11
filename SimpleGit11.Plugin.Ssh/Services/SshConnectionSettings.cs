using System;
using System.Collections.Generic;
using System.Globalization;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Plugin.Ssh.Services;

public sealed record SshConnectionSettings(
    string Host,
    int Port,
    string Username,
    string? Password,
    string? PrivateKeyPath,
    string? PrivateKeyPassphrase,
    string? ExpectedHostKey)
{
    public static SshConnectionSettings FromRequest(ExecutionConnectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string host = GetRequired(request, SshConnectionRequestKeys.Host);
        string username = GetRequired(request, SshConnectionRequestKeys.Username);
        int port = 22;
        if (request.Settings.TryGetValue(SshConnectionRequestKeys.Port, out string? portText) &&
            (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) ||
             port is < 1 or > 65535))
        {
            throw new ArgumentException("SSH port must be a number from 1 to 65535.", nameof(request));
        }

        string? privateKeyPath = GetOptional(request.Settings, SshConnectionRequestKeys.PrivateKeyPath);
        string? password = request.Secrets is null
            ? null
            : GetOptional(request.Secrets, SshConnectionRequestKeys.Password);
        string? passphrase = request.Secrets is null
            ? null
            : GetOptional(request.Secrets, SshConnectionRequestKeys.PrivateKeyPassphrase);
        if (string.IsNullOrWhiteSpace(privateKeyPath) && password is null)
        {
            throw new ArgumentException(
                "Either an SSH password or a private key path must be provided.",
                nameof(request));
        }

        return new SshConnectionSettings(
            host,
            port,
            username,
            password,
            privateKeyPath,
            passphrase,
            GetOptional(request.Settings, SshConnectionRequestKeys.ExpectedHostKey));
    }

    private static string GetRequired(ExecutionConnectionRequest request, string key)
    {
        if (!request.Settings.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"SSH setting '{key}' is required.", nameof(request));
        }

        return value.Trim();
    }

    private static string? GetOptional(
        IReadOnlyDictionary<string, string> values,
        string key)
    {
        return values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }
}
