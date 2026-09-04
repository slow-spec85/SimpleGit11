using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services;

public sealed class GitConfigService : IGitConfigService
{
    private const string CredentialHelperKey = "credential.helper";
    private const string CredentialManagerHelperValue = "manager";
    private const string PushDefaultRemoteKey = "remote.pushDefault";
    private const string PullRebaseKey = "pull.rebase";
    private const string PullFastForwardKey = "pull.ff";
    private const string SshCommandKey = "core.sshCommand";
    private const string UrlRewriteKeyPrefix = "url.";
    private const string UrlRewriteKeySuffix = ".insteadof";
    private readonly IGitCommandRunner _commandRunner;

    public GitConfigService(IGitCommandRunner? commandRunner = null)
    {
        _commandRunner = commandRunner ?? new GitCommandRunner();
    }

    public Task<string> GetUserNameAsync(ConfigScope level, RepositoryInfo? repository)
    {
        List<string> args = ["config", level == ConfigScope.Global ? "--global" : level == ConfigScope.Local ? "--local" : "user.name"];
        if (level > ConfigScope.None)
            args.Add("user.name");

        return RunGitAsync(repository, [.. args], level != ConfigScope.Global, throwOnError: false);
    }

    public Task<string> GetUserEmailAsync(ConfigScope level, RepositoryInfo? repository)
    {
        List<string> args = ["config", level == ConfigScope.Global ? "--global" : level == ConfigScope.Local ? "--local" : "user.email"];
        if (level > ConfigScope.None)
            args.Add("user.email");

        return RunGitAsync(repository, [.. args], level != ConfigScope.Global, throwOnError: false);
    }

    public Task<string> GetCredentialHelperAsync(ConfigScope level, RepositoryInfo? repository)
    {
        List<string> args = ["config", level == ConfigScope.Global ? "--global" : level == ConfigScope.Local ? "--local" : "--get"];
        if (level > ConfigScope.None)
            args.Add("--get");

        return RunGitAsync(repository, [.. args, CredentialHelperKey], level != ConfigScope.Global, throwOnError: false);
    }

    public async Task<bool> IsGlobalCredentialHelperManagerConfiguredAsync()
    {
        string output = await RunGitAsync(
            null,
            ["config", "--global", "--get-all", CredentialHelperKey],
            repoNeeded: false,
            throwOnError: false);

        string[] helpers = output.Split(['\r', '\n'], System.StringSplitOptions.RemoveEmptyEntries);
        return helpers.Length == 1
            && helpers[0].Trim().Equals(CredentialManagerHelperValue, System.StringComparison.OrdinalIgnoreCase);
    }

    public Task<string> GetInitialBranchNameAsync(ConfigScope level, RepositoryInfo? repository)
    {
        List<string> args = ["config", level == ConfigScope.Global ? "--global" : level == ConfigScope.Local ? "--local" : "--get"];
        if (level > ConfigScope.None)
            args.Add("--get");

        return RunGitAsync(repository, [.. args, "init.defaultBranch"], level != ConfigScope.Global, throwOnError: false);
    }

    public Task<string> GetPushDefaultRemoteAsync(ConfigScope level, RepositoryInfo? repository)
    {
        List<string> args = ["config", level == ConfigScope.Global ? "--global" : level == ConfigScope.Local ? "--local" : "--get"];
        if (level > ConfigScope.None)
            args.Add("--get");

        return RunGitAsync(repository, [.. args, PushDefaultRemoteKey], level != ConfigScope.Global, throwOnError: false);
    }

    public Task<string> GetGlobalSshCommandAsync()
    {
        return RunGitAsync(
            null,
            ["config", "--global", "--get", SshCommandKey],
            repoNeeded: false,
            throwOnError: false);
    }

    public async Task<GitPullSettings> GetPullSettingsAsync(ConfigScope level, RepositoryInfo? repository)
    {
        string? rebase = await ReadPullValueAsync(level, repository, PullRebaseKey);
        string? fastForward = await ReadPullValueAsync(level, repository, PullFastForwardKey);
        return new GitPullSettings(rebase, fastForward);
    }

    public Task SetPullRebaseAsync(ConfigScope level, RepositoryInfo? repository, string? value)
    {
        if (value is not (null or "false" or "true" or "merges" or "interactive"))
        {
            throw new ArgumentException("Unsupported pull.rebase value.", nameof(value));
        }

        return WritePullValueAsync(level, repository, PullRebaseKey, value);
    }

    public Task SetPullFastForwardAsync(ConfigScope level, RepositoryInfo? repository, string? value)
    {
        if (value is not (null or "false" or "true" or "only"))
        {
            throw new ArgumentException("Unsupported pull.ff value.", nameof(value));
        }

        return WritePullValueAsync(level, repository, PullFastForwardKey, value);
    }

    private async Task<string?> ReadPullValueAsync(ConfigScope level, RepositoryInfo? repository, string key)
    {
        GitCommandResult result = await RunPullConfigAsync(level, repository, ["--null", "--get", key]);
        if (result.ExitCode == 1)
        {
            return null;
        }

        EnsurePullConfigSucceeded(result);
        return result.StandardOutput.TrimEnd('\0');
    }

    private async Task WritePullValueAsync(
        ConfigScope level, RepositoryInfo? repository, string key, string? value)
    {
        string[] arguments = value is null ? ["--unset-all", key] : ["--replace-all", key, value];
        GitCommandResult result = await RunPullConfigAsync(level, repository, arguments);
        // Git returns 5 when an unset key is already absent; other failures must be surfaced.
        if (value is null && result.ExitCode == 5)
        {
            return;
        }

        EnsurePullConfigSucceeded(result);
    }

    private Task<GitCommandResult> RunPullConfigAsync(
        ConfigScope level, RepositoryInfo? repository, string[] arguments)
    {
        string scope = level switch
        {
            ConfigScope.Global => "--global",
            ConfigScope.Local => "--local",
            _ => throw new ArgumentOutOfRangeException(nameof(level))
        };
        if (level == ConfigScope.Local)
        {
            ArgumentNullException.ThrowIfNull(repository);
        }

        return _commandRunner.RunAsync(
            level == ConfigScope.Local ? repository!.Path : Environment.CurrentDirectory,
            ["config", scope, .. arguments],
            new GitCommandOptions(ThrowOnError: false, UseDefaultWorkingDirectory: level == ConfigScope.Global));
    }

    private static void EnsurePullConfigSucceeded(GitCommandResult result)
    {
        if (!result.IsSuccess)
        {
            throw new GitCommandException(result.CombinedOutput, result.ExitCode);
        }
    }

    public async Task<IReadOnlyList<GitUrlRewrite>> GetGlobalUrlRewritesAsync()
    {
        string output = await RunGitAsync(
            null,
            ["config", "--global", "--null", "--get-regexp", "^url\\..*\\.insteadof$"],
            repoNeeded: false,
            throwOnError: false,
            trimOutput: false);
        List<GitUrlRewrite> rewrites = [];
        foreach (string entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int separatorIndex = entry.IndexOf('\n');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = entry[..separatorIndex];
            if (!key.StartsWith(UrlRewriteKeyPrefix, StringComparison.OrdinalIgnoreCase)
                || !key.EndsWith(UrlRewriteKeySuffix, StringComparison.OrdinalIgnoreCase)
                || key.Length <= UrlRewriteKeyPrefix.Length + UrlRewriteKeySuffix.Length)
            {
                continue;
            }

            string replacementUrl = key[UrlRewriteKeyPrefix.Length..^UrlRewriteKeySuffix.Length];
            string insteadOfUrl = entry[(separatorIndex + 1)..];
            if (!string.IsNullOrWhiteSpace(replacementUrl)
                && !string.IsNullOrWhiteSpace(insteadOfUrl))
            {
                rewrites.Add(new GitUrlRewrite(insteadOfUrl, replacementUrl));
            }
        }

        return rewrites
            .OrderBy(rewrite => rewrite.InsteadOfUrl, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rewrite => rewrite.ReplacementUrl, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<IReadOnlyDictionary<string, string>> GetBranchDescriptionsAsync(RepositoryInfo repository)
    {
        return GetBranchValuesAsync(repository, "description");
    }

    public Task<IReadOnlyDictionary<string, string>> GetBranchPushRemotesAsync(RepositoryInfo repository)
    {
        return GetBranchValuesAsync(repository, "pushRemote");
    }

    private async Task<IReadOnlyDictionary<string, string>> GetBranchValuesAsync(
        RepositoryInfo repository,
        string propertyName)
    {
        string output = await RunGitAsync(
            repository,
            ["config", "--local", "--null", "--get-regexp", $"^branch\\..*\\.{propertyName}$"],
            throwOnError: false,
            trimOutput: false);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (string entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int separatorIndex = entry.IndexOf('\n');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = entry[..separatorIndex];
            const string prefix = "branch.";
            string suffix = $".{propertyName}";
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                || key.Length <= prefix.Length + suffix.Length)
            {
                continue;
            }

            string branchName = key[prefix.Length..^suffix.Length];
            values[branchName] = entry[(separatorIndex + 1)..].Trim();
        }

        return values;
    }

    public Task SetUserNameAsync(ConfigScope level, RepositoryInfo? repository, string userName)
    {
        List<string> args = ["config", level == ConfigScope.Global ? "--global" : level == ConfigScope.Local ? "--local" : "user.name"];
        if (level > ConfigScope.None)
            args.Add("user.name");

        return RunGitAsync(repository, [.. args, userName], level != ConfigScope.Global);
    }

    public Task SetUserEmailAsync(ConfigScope level, RepositoryInfo? repository, string userEmail)
    {
        List<string> args = ["config", level == ConfigScope.Global ? "--global" : level == ConfigScope.Local ? "--local" : "user.email"];
        if (level > ConfigScope.None)
            args.Add("user.email");

        return RunGitAsync(repository, [.. args, userEmail], level != ConfigScope.Global);
    }

    public Task SetCredentialHelperAsync(ConfigScope level, RepositoryInfo? repository)
    {
        List<string> args = ["config", level == ConfigScope.Global ? "--global" : level == ConfigScope.Local ? "--local" : CredentialHelperKey];
        if (level > ConfigScope.None)
            args.Add(CredentialHelperKey);

        return RunGitAsync(repository, [.. args, CredentialManagerHelperValue], level != ConfigScope.Global);
    }

    public Task SetGlobalCredentialHelperManagerAsync()
    {
        return RunGitAsync(
            null,
            ["config", "--global", "--replace-all", CredentialHelperKey, CredentialManagerHelperValue],
            repoNeeded: false);
    }

    public Task UnsetGlobalCredentialHelperAsync()
    {
        return RunGitAsync(
            null,
            ["config", "--global", "--unset-all", CredentialHelperKey],
            repoNeeded: false,
            throwOnError: false);
    }

    public Task AddGlobalUrlRewriteAsync(GitUrlRewrite rewrite)
    {
        GitUrlRewrite normalizedRewrite = NormalizeUrlRewrite(rewrite);
        return RunGitAsync(
            null,
            [
                "config",
                "--global",
                "--add",
                GetUrlRewriteKey(normalizedRewrite),
                normalizedRewrite.InsteadOfUrl
            ],
            repoNeeded: false);
    }

    public async Task UpdateGlobalUrlRewriteAsync(
        GitUrlRewrite oldRewrite,
        GitUrlRewrite newRewrite)
    {
        GitUrlRewrite normalizedOldRewrite = NormalizeUrlRewrite(oldRewrite);
        GitUrlRewrite normalizedNewRewrite = NormalizeUrlRewrite(newRewrite);
        if (normalizedOldRewrite == normalizedNewRewrite)
        {
            return;
        }

        await AddGlobalUrlRewriteAsync(normalizedNewRewrite);
        try
        {
            await RemoveGlobalUrlRewriteCoreAsync(normalizedOldRewrite, throwOnError: true);
        }
        catch
        {
            await RemoveGlobalUrlRewriteCoreAsync(normalizedNewRewrite, throwOnError: false);
            throw;
        }
    }

    public Task RemoveGlobalUrlRewriteAsync(GitUrlRewrite rewrite)
    {
        return RemoveGlobalUrlRewriteCoreAsync(NormalizeUrlRewrite(rewrite), throwOnError: true);
    }

    public Task SetInitialBranchNameAsync(ConfigScope level, RepositoryInfo? repository, string branchName)
    {
        List<string> args = ["config", level == ConfigScope.Global ? "--global" : level == ConfigScope.Local ? "--local" : "init.defaultBranch"];
        if (level > ConfigScope.None)
            args.Add("init.defaultBranch");

        return RunGitAsync(repository, [.. args, branchName], level != ConfigScope.Global);
    }

    public Task SetPushDefaultRemoteAsync(
        ConfigScope level,
        RepositoryInfo? repository,
        string remoteName)
    {
        List<string> args =
        [
            "config",
            level == ConfigScope.Global ? "--global" : level == ConfigScope.Local ? "--local" : PushDefaultRemoteKey
        ];
        if (level > ConfigScope.None)
            args.Add(PushDefaultRemoteKey);

        return RunGitAsync(repository, [.. args, remoteName], level != ConfigScope.Global);
    }

    public Task SetGlobalSshCommandAsync(string sshCommand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sshCommand);
        return RunGitAsync(
            null,
            ["config", "--global", "--replace-all", SshCommandKey, sshCommand.Trim()],
            repoNeeded: false);
    }

    public async Task SetBranchUpstreamAsync(
        RepositoryInfo repository,
        string branchName,
        string remoteName)
    {
        string mergeKey = $"branch.{branchName}.merge";
        string existingMerge = await RunGitAsync(
            repository,
            ["config", "--local", "--get", mergeKey],
            throwOnError: false);
        await RunGitAsync(
            repository,
            ["config", "--local", "--replace-all", $"branch.{branchName}.remote", remoteName]);
        if (string.IsNullOrWhiteSpace(existingMerge))
        {
            await RunGitAsync(
                repository,
                ["config", "--local", "--replace-all", mergeKey, $"refs/heads/{branchName}"]);
        }
    }

    public async Task UnsetBranchUpstreamAsync(RepositoryInfo repository, string branchName)
    {
        await RunGitAsync(
            repository,
            ["config", "--local", "--unset-all", $"branch.{branchName}.remote"],
            throwOnError: false);
        await RunGitAsync(
            repository,
            ["config", "--local", "--unset-all", $"branch.{branchName}.merge"],
            throwOnError: false);
    }

    public Task SetBranchPushRemoteAsync(
        RepositoryInfo repository,
        string branchName,
        string? remoteName)
    {
        string key = $"branch.{branchName}.pushRemote";
        return string.IsNullOrWhiteSpace(remoteName)
            ? RunGitAsync(repository, ["config", "--local", "--unset-all", key], throwOnError: false)
            : RunGitAsync(repository, ["config", "--local", "--replace-all", key, remoteName.Trim()]);
    }

    public Task SetBranchDescriptionAsync(
        RepositoryInfo repository,
        string branchName,
        string description)
    {
        string key = $"branch.{branchName}.description";
        return string.IsNullOrWhiteSpace(description)
            ? RunGitAsync(repository, ["config", "--local", "--unset-all", key], throwOnError: false)
            : RunGitAsync(repository, ["config", "--local", "--replace-all", key, description.Trim()]);
    }

    public Task UnsetUserNameAsync(ConfigScope level, RepositoryInfo? repository)
    {
        return UnsetIdentityValueAsync(level, repository, "user.name");
    }

    public Task UnsetUserEmailAsync(ConfigScope level, RepositoryInfo? repository)
    {
        return UnsetIdentityValueAsync(level, repository, "user.email");
    }

    private async Task UnsetIdentityValueAsync(ConfigScope level, RepositoryInfo? repository, string key)
    {
        List<string> arguments = ["config"];
        if (level != ConfigScope.None)
        {
            arguments.Add(level == ConfigScope.Global ? "--global" : "--local");
        }

        try
        {
            await RunGitAsync(repository, [.. arguments, "--unset-all", key], level != ConfigScope.Global);
        }
        catch (GitCommandException exception) when (exception.ExitCode == 5)
        {
            // Saving other settings must also work when no identity is configured in this scope.
        }
    }

    public Task UnsetCredentialHelperAsync(ConfigScope level, RepositoryInfo? repository)
    {
        List<string> args = ["config", level == ConfigScope.Global ? "--global" : level == ConfigScope.Local ? "--local" : "--unset"];
        if (level > ConfigScope.None)
            args.Add("--unset");

        return RunGitAsync(repository, [.. args, CredentialHelperKey], level != ConfigScope.Global);
    }

    public Task UnsetInitialBranchNameAsync(ConfigScope level, RepositoryInfo? repository)
    {
        List<string> args = ["config", level == ConfigScope.Global ? "--global" : level == ConfigScope.Local ? "--local" : "--unset"];
        if (level > ConfigScope.None)
            args.Add("--unset");

        return RunGitAsync(repository, [.. args, "init.defaultBranch"], level != ConfigScope.Global, throwOnError: false);
    }

    public Task UnsetPushDefaultRemoteAsync(ConfigScope level, RepositoryInfo? repository)
    {
        List<string> args = ["config", level == ConfigScope.Global ? "--global" : level == ConfigScope.Local ? "--local" : "--unset"];
        if (level > ConfigScope.None)
            args.Add("--unset");

        return RunGitAsync(repository, [.. args, PushDefaultRemoteKey], level != ConfigScope.Global, throwOnError: false);
    }

    public Task UnsetGlobalSshCommandAsync()
    {
        return RunGitAsync(
            null,
            ["config", "--global", "--unset-all", SshCommandKey],
            repoNeeded: false,
            throwOnError: false);
    }


    private async Task<string> RunGitAsync(
        RepositoryInfo? repository,
        string[] arguments,
        bool repoNeeded = true,
        bool throwOnError = true,
        bool trimOutput = true)
    {
        string workingDirectory = repoNeeded && repository is not null
            ? repository.Path
            : Environment.CurrentDirectory;
        GitCommandResult result = await _commandRunner.RunAsync(
            workingDirectory,
            arguments,
            new GitCommandOptions(
                ThrowOnError: throwOnError,
                UseDefaultWorkingDirectory: !repoNeeded));
        return trimOutput ? result.StandardOutput.Trim() : result.StandardOutput;
    }

    private Task RemoveGlobalUrlRewriteCoreAsync(GitUrlRewrite rewrite, bool throwOnError)
    {
        return RunGitAsync(
            null,
            [
                "config",
                "--global",
                "--fixed-value",
                "--unset-all",
                GetUrlRewriteKey(rewrite),
                rewrite.InsteadOfUrl
            ],
            repoNeeded: false,
            throwOnError: throwOnError);
    }

    private static GitUrlRewrite NormalizeUrlRewrite(GitUrlRewrite rewrite)
    {
        ArgumentNullException.ThrowIfNull(rewrite);
        string insteadOfUrl = rewrite.InsteadOfUrl.Trim();
        string replacementUrl = rewrite.ReplacementUrl.Trim();
        ArgumentException.ThrowIfNullOrWhiteSpace(insteadOfUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementUrl);
        if (ContainsConfigLineBreak(insteadOfUrl) || ContainsConfigLineBreak(replacementUrl))
        {
            throw new ArgumentException("Git URL rewrite values must not contain line breaks.");
        }

        return new GitUrlRewrite(insteadOfUrl, replacementUrl);
    }

    private static string GetUrlRewriteKey(GitUrlRewrite rewrite)
    {
        return $"{UrlRewriteKeyPrefix}{rewrite.ReplacementUrl}.insteadOf";
    }

    private static bool ContainsConfigLineBreak(string value)
    {
        return value.IndexOfAny(['\0', '\r', '\n']) >= 0;
    }
}
