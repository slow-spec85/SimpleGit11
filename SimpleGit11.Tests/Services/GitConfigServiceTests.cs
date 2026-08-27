using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Services;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitConfigServiceTests
{
    [TestMethod]
    public async Task SetBranchPushRemoteAsync_SetAndReset_UpdatesLocalConfiguration()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        GitConfigService service = new();

        await service.SetBranchPushRemoteAsync(repository.Repository, "feature/topic", "public");

        IReadOnlyDictionary<string, string> configuredRemotes =
            await service.GetBranchPushRemotesAsync(repository.Repository);
        Assert.AreEqual("public", configuredRemotes["feature/topic"]);

        await service.SetBranchPushRemoteAsync(repository.Repository, "feature/topic", null);

        configuredRemotes = await service.GetBranchPushRemotesAsync(repository.Repository);
        Assert.IsFalse(configuredRemotes.ContainsKey("feature/topic"));
    }

    [TestMethod]
    public async Task SetBranchUpstreamAsync_ChangesRemoteAndPreservesMergeBranch()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        GitConfigService service = new();
        await repository.RunGitAsync(
            "config",
            "--local",
            "branch.feature.remote",
            "origin");
        await repository.RunGitAsync(
            "config",
            "--local",
            "branch.feature.merge",
            "refs/heads/release");

        await service.SetBranchUpstreamAsync(repository.Repository, "feature", "public");

        string remote = await repository.RunGitAsync(
            "config",
            "--local",
            "--get",
            "branch.feature.remote");
        string merge = await repository.RunGitAsync(
            "config",
            "--local",
            "--get",
            "branch.feature.merge");
        Assert.AreEqual("public", remote);
        Assert.AreEqual("refs/heads/release", merge);
    }

    [TestMethod]
    public async Task UnsetBranchUpstreamAsync_RemovesRemoteAndMergeBranch()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        GitConfigService service = new();
        await repository.RunGitAsync(
            "config",
            "--local",
            "branch.feature.remote",
            "origin");
        await repository.RunGitAsync(
            "config",
            "--local",
            "branch.feature.merge",
            "refs/heads/release");

        await service.UnsetBranchUpstreamAsync(repository.Repository, "feature");

        string configuration = await repository.RunGitAsync("config", "--local", "--list");
        Assert.IsFalse(configuration.Contains(
            "branch.feature.remote",
            System.StringComparison.Ordinal));
        Assert.IsFalse(configuration.Contains(
            "branch.feature.merge",
            System.StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SetPushDefaultRemoteAsync_SetAndReset_UpdatesConfiguration()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        GitConfigService service = new();

        await service.SetPushDefaultRemoteAsync(ConfigScope.Local, repository.Repository, "public");

        string configuredRemote = await service.GetPushDefaultRemoteAsync(
            ConfigScope.Local,
            repository.Repository);
        Assert.AreEqual("public", configuredRemote);

        await service.UnsetPushDefaultRemoteAsync(ConfigScope.Local, repository.Repository);

        configuredRemote = await service.GetPushDefaultRemoteAsync(
            ConfigScope.Local,
            repository.Repository);
        Assert.AreEqual("", configuredRemote);
    }

    [TestMethod]
    public async Task GetGlobalUrlRewritesAsync_ParsesAndSortsMappings()
    {
        RecordingGitCommandRunner runner = new()
        {
            QueryOutput = string.Join('\0',
                "url.ssh://private.example/b.git.insteadof\nhttps://public.example/b.git",
                "url.ssh://private.example/a.git.insteadof\nhttps://public.example/a.git",
                "")
        };
        GitConfigService service = new(runner);

        IReadOnlyList<GitUrlRewrite> rewrites = await service.GetGlobalUrlRewritesAsync();

        Assert.HasCount(2, rewrites);
        Assert.AreEqual("https://public.example/a.git", rewrites[0].InsteadOfUrl);
        Assert.AreEqual("ssh://private.example/a.git", rewrites[0].ReplacementUrl);
        Assert.AreEqual(
            "config --global --null --get-regexp ^url\\..*\\.insteadof$",
            runner.Commands[0]);
    }

    [TestMethod]
    public async Task GlobalUrlRewriteOperations_UseExactGlobalConfigurationValues()
    {
        RecordingGitCommandRunner runner = new();
        GitConfigService service = new(runner);
        GitUrlRewrite oldRewrite = new(
            "https://public.example/library.git",
            "ssh://private.example/library.git");
        GitUrlRewrite newRewrite = new(
            "https://public.example/library.git",
            "ssh://new-private.example/library.git");

        await service.AddGlobalUrlRewriteAsync(oldRewrite);
        await service.UpdateGlobalUrlRewriteAsync(oldRewrite, newRewrite);
        await service.RemoveGlobalUrlRewriteAsync(newRewrite);

        CollectionAssert.AreEqual(
            new[]
            {
                "config --global --add url.ssh://private.example/library.git.insteadOf https://public.example/library.git",
                "config --global --add url.ssh://new-private.example/library.git.insteadOf https://public.example/library.git",
                "config --global --fixed-value --unset-all url.ssh://private.example/library.git.insteadOf https://public.example/library.git",
                "config --global --fixed-value --unset-all url.ssh://new-private.example/library.git.insteadOf https://public.example/library.git"
            },
            runner.Commands);
    }

    [TestMethod]
    public async Task GlobalSshCommandOperations_UseCoreSshCommandSetting()
    {
        RecordingGitCommandRunner runner = new()
        {
            QueryOutput = "C:/Windows/System32/OpenSSH/ssh.exe\n"
        };
        GitConfigService service = new(runner);

        string configuredCommand = await service.GetGlobalSshCommandAsync();
        await service.SetGlobalSshCommandAsync(" C:/Windows/System32/OpenSSH/ssh.exe ");
        await service.UnsetGlobalSshCommandAsync();

        Assert.AreEqual("C:/Windows/System32/OpenSSH/ssh.exe", configuredCommand);
        CollectionAssert.AreEqual(
            new[]
            {
                "config --global --get core.sshCommand",
                "config --global --replace-all core.sshCommand C:/Windows/System32/OpenSSH/ssh.exe",
                "config --global --unset-all core.sshCommand"
            },
            runner.Commands);
    }

    private sealed class RecordingGitCommandRunner : IGitCommandRunner
    {
        public List<string> Commands { get; } = [];

        public string QueryOutput { get; init; } = "";

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandOptions? options = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(string.Join(' ', arguments));
            string output = arguments.Contains("--get-regexp") || arguments.Contains("--get")
                ? QueryOutput
                : "";
            return Task.FromResult(new GitCommandResult(0, output, ""));
        }
    }
}
