using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services.Execution;

public sealed class ContextualGitCommandRunner : IGitCommandRunner
{
    private readonly IExecutionContextService _executionContextService;
    private readonly ILocalGitCredentialService? _credentialService;

    public ContextualGitCommandRunner(
        IExecutionContextService executionContextService,
        ILocalGitCredentialService? credentialService = null)
    {
        _executionContextService = executionContextService;
        _credentialService = credentialService;
    }

    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        GitCommandOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ExecutionContext context = _executionContextService.Current;
        options ??= new GitCommandOptions();
        if (context.IsLocal
            || _credentialService is null
            || options.HttpAuthentication is not { } authentication
            || !IsHttpsUrl(authentication.Url))
        {
            return await context.Runtime.Git.RunAsync(
                workingDirectory,
                arguments,
                options,
                cancellationToken);
        }

        GitCommandOptions nonInteractiveOptions = options with
        {
            ThrowOnError = false,
            HttpAuthentication = new GitHttpAuthentication(authentication.Url)
        };
        GitCommandResult result = await context.Runtime.Git.RunAsync(
            workingDirectory,
            arguments,
            nonInteractiveOptions,
            cancellationToken);
        if (result.IsSuccess || !IsAuthenticationFailure(result))
        {
            return Complete(result, options.ThrowOnError);
        }

        GitHttpAuthentication? credential;
        try
        {
            credential = await _credentialService.GetAsync(
                authentication.Url,
                cancellationToken);
        }
        catch (GitCommandException exception)
        {
            return Complete(
                CreateCredentialManagerFailureResult(result, exception),
                options.ThrowOnError);
        }

        for (int attempt = 0; credential is not null && attempt < 2; attempt++)
        {
            result = await context.Runtime.Git.RunAsync(
                workingDirectory,
                arguments,
                nonInteractiveOptions with { HttpAuthentication = credential },
                cancellationToken);
            if (result.IsSuccess)
            {
                await _credentialService.ApproveAsync(credential, cancellationToken);
                return result;
            }

            if (!IsAuthenticationFailure(result))
            {
                return Complete(result, options.ThrowOnError);
            }

            await _credentialService.RejectAsync(credential, cancellationToken);
            if (attempt == 0)
            {
                try
                {
                    credential = await _credentialService.GetAsync(
                        authentication.Url,
                        cancellationToken);
                }
                catch (GitCommandException exception)
                {
                    return Complete(
                        CreateCredentialManagerFailureResult(result, exception),
                        options.ThrowOnError);
                }
            }
            else
            {
                credential = null;
            }
        }

        return Complete(result, options.ThrowOnError);
    }

    private static bool IsHttpsUrl(string remoteUrl)
    {
        return Uri.TryCreate(remoteUrl, UriKind.Absolute, out Uri? uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAuthenticationFailure(GitCommandResult result)
    {
        return GitRemoteOperationErrorClassifier.Classify(result.CombinedOutput)
            == GitRemoteOperationErrorKind.Authentication;
    }

    private static GitCommandResult CreateCredentialManagerFailureResult(
        GitCommandResult remoteResult,
        GitCommandException credentialException)
    {
        string remoteError = remoteResult.CombinedOutput;
        string combinedError = string.IsNullOrWhiteSpace(remoteError)
            ? credentialException.Message
            : $"{credentialException.Message}{Environment.NewLine}{Environment.NewLine}{remoteError}";
        return new GitCommandResult(
            credentialException.ExitCode == 0 ? remoteResult.ExitCode : credentialException.ExitCode,
            "",
            combinedError);
    }

    private static GitCommandResult Complete(GitCommandResult result, bool throwOnError)
    {
        if (!result.IsSuccess && throwOnError)
        {
            string message = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput.Trim()
                : result.StandardError.Trim();
            throw new GitCommandException(message, result.ExitCode);
        }

        return result;
    }
}
