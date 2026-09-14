using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Services.Git.Execution;

public sealed class LocalGitCredentialService : ILocalGitCredentialService
{
    private readonly IGitCommandRunner _commandRunner;
    private readonly ConditionalWeakTable<GitHttpAuthentication, StrongBox<string>> _credentialInputs = new();

    public LocalGitCredentialService(GitCommandRunner commandRunner)
        : this((IGitCommandRunner)commandRunner)
    {
    }

    internal LocalGitCredentialService(IGitCommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<GitHttpAuthentication?> GetAsync(
        string remoteUrl,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetHttpsUrl(remoteUrl, out string normalizedUrl))
        {
            return null;
        }

        GitCommandResult result = await RunCredentialCommandAsync(
            "fill",
            CreateRequest(normalizedUrl),
            cancellationToken);
        if (!result.IsSuccess)
        {
            string error = string.IsNullOrWhiteSpace(result.StandardError)
                ? $"Git Credential Manager exited with code {result.ExitCode} without returning credentials."
                : $"Git Credential Manager failed.{Environment.NewLine}{result.StandardError.Trim()}";
            throw new GitCommandException(error, result.ExitCode);
        }

        IReadOnlyDictionary<string, string> values = ParseResponse(result.StandardOutput);
        if (!values.TryGetValue("username", out string? username)
            || !values.TryGetValue("password", out string? password)
            || string.IsNullOrEmpty(username)
            || string.IsNullOrEmpty(password))
        {
            throw new GitCommandException(
                "Git Credential Manager did not return both a username and a password.",
                -1);
        }

        GitHttpAuthentication credential = new(normalizedUrl, username, password);
        _credentialInputs.Add(
            credential,
            new StrongBox<string>(CompleteRequest(result.StandardOutput)));
        return credential;
    }

    public async Task ApproveAsync(
        GitHttpAuthentication credential,
        CancellationToken cancellationToken = default)
    {
        await StoreAsync("approve", credential, cancellationToken);
    }

    public async Task RejectAsync(
        GitHttpAuthentication credential,
        CancellationToken cancellationToken = default)
    {
        await StoreAsync("reject", credential, cancellationToken);
    }

    private async Task StoreAsync(
        string action,
        GitHttpAuthentication credential,
        CancellationToken cancellationToken)
    {
        if (!TryGetHttpsUrl(credential.Url, out string normalizedUrl)
            || string.IsNullOrEmpty(credential.Username)
            || string.IsNullOrEmpty(credential.Password))
        {
            return;
        }

        bool hasFilledCredential = _credentialInputs.TryGetValue(
            credential,
            out StrongBox<string>? filledCredential);
        _credentialInputs.Remove(credential);
        string? filledRequest = hasFilledCredential ? filledCredential?.Value : null;
        string request = filledRequest
            ?? CreateRequest(
                normalizedUrl,
                credential.Username,
                credential.Password);
        _ = await RunCredentialCommandAsync(action, request, cancellationToken);
    }

    private Task<GitCommandResult> RunCredentialCommandAsync(
        string action,
        string standardInput,
        CancellationToken cancellationToken)
    {
        return _commandRunner.RunAsync(
            Path.GetTempPath(),
            [
                "-c",
                "credential.credentialStore=dpapi",
                "credential",
                action
            ],
            new GitCommandOptions(
                StandardInput: standardInput,
                ThrowOnError: false,
                UseDefaultWorkingDirectory: true),
            cancellationToken);
    }

    private static bool TryGetHttpsUrl(string remoteUrl, out string normalizedUrl)
    {
        normalizedUrl = "";
        if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalizedUrl = uri.AbsoluteUri;
        return true;
    }

    private static string CreateRequest(
        string remoteUrl,
        string? username = null,
        string? password = null)
    {
        ValidateCredentialValue(remoteUrl);
        ValidateCredentialValue(username);
        ValidateCredentialValue(password);
        StringBuilder request = new();
        request.Append("url=").Append(remoteUrl).Append('\n');
        if (username is not null)
        {
            request.Append("username=").Append(username).Append('\n');
        }

        if (password is not null)
        {
            request.Append("password=").Append(password).Append('\n');
        }

        request.Append('\n');
        return request.ToString();
    }

    private static IReadOnlyDictionary<string, string> ParseResponse(string output)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            int separatorIndex = line.IndexOf('=');
            if (separatorIndex > 0)
            {
                values[line[..separatorIndex]] = line[(separatorIndex + 1)..];
            }
        }

        return values;
    }

    private static string CompleteRequest(string request)
    {
        return request.TrimEnd('\r', '\n') + "\n\n";
    }

    private static void ValidateCredentialValue(string? value)
    {
        if (value?.IndexOfAny(['\r', '\n']) >= 0)
        {
            throw new ArgumentException("Git credential values cannot contain line breaks.");
        }
    }
}
