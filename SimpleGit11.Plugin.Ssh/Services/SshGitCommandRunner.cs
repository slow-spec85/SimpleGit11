using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Services;
using SimpleGit11.Services.Execution;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Plugin.Ssh.Services;

public sealed class SshGitCommandRunner : IGitCommandRunner
{
    private readonly SshCommandSession _session;
    private readonly RepositoryPathStyle _pathStyle;

    public SshGitCommandRunner(SshCommandSession session, RepositoryPathStyle pathStyle)
    {
        _session = session;
        _pathStyle = pathStyle;
    }

    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        GitCommandOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new GitCommandOptions();
        string commandText = RemoteCommandComposer.ComposeGit(
            _pathStyle,
            workingDirectory,
            arguments,
            options.EnvironmentVariables,
            options.UseDefaultWorkingDirectory,
            options.HttpAuthentication);
        string? standardInput = options.HttpAuthentication is
        {
            Username: not null,
            Password: not null
        } credential
            ? CreateCredentialInput(credential)
            : options.StandardInput;
        SshCommandResult commandResult = await _session.ExecuteAsync(
            commandText,
            standardInput,
            cancellationToken);
        GitCommandResult result = new(
            commandResult.ExitCode,
            commandResult.StandardOutput,
            commandResult.StandardError);
        if (!result.IsSuccess && options.ThrowOnError)
        {
            string message = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput.Trim()
                : result.StandardError.Trim();
            throw new GitCommandException(message, result.ExitCode);
        }

        return result;
    }

    private static string CreateCredentialInput(GitHttpAuthentication credential)
    {
        ValidateCredentialValue(credential.Username!);
        ValidateCredentialValue(credential.Password!);
        return $"username={credential.Username}\npassword={credential.Password}\n\n";
    }

    private static void ValidateCredentialValue(string value)
    {
        if (value.IndexOfAny(['\r', '\n']) >= 0)
        {
            throw new ArgumentException("Git credential values cannot contain line breaks.");
        }
    }
}
