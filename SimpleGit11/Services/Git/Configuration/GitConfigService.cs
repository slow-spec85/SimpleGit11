using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
        List<string> args = ["config", level == ConfigScope.Global ? "--global" : level == ConfigScope.Local ? "--local" : "--unset"];
        if (level > ConfigScope.None)
            args.Add("--unset");

        return RunGitAsync(repository, [.. args, "user.name"], level != ConfigScope.Global);
    }

    public Task UnsetUserEmailAsync(ConfigScope level, RepositoryInfo? repository)
    {
        List<string> args = ["config", level == ConfigScope.Global ? "--global" : level == ConfigScope.Local ? "--local" : "--unset"];
        if (level > ConfigScope.None)
            args.Add("--unset");

        return RunGitAsync(repository, [.. args, "user.email"], level != ConfigScope.Global);
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
            new GitCommandOptions(ThrowOnError: throwOnError));
        return trimOutput ? result.StandardOutput.Trim() : result.StandardOutput;
    }
}
