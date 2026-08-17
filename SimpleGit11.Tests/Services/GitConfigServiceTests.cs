using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Services;
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
}
