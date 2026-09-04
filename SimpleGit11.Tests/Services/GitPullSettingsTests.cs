using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitPullSettingsTests
{
    [TestMethod]
    [DataRow("false")]
    [DataRow("true")]
    public async Task PullSettings_DivergentBranchesFollowRebaseChoiceWhenFastForwardIsTrue(string rebase)
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        using TemporaryDirectory configurationDirectory = new();
        IsolatedConfigRunner runner = new(configurationDirectory.CreateFile("global.gitconfig"));
        GitConfigService service = new(runner);
        repository.WriteFile("base.txt", "base");
        await repository.CommitAllAsync("base");
        await repository.RunGitAsync("checkout", "-b", "incoming");
        repository.WriteFile("incoming.txt", "incoming");
        await repository.CommitAllAsync("incoming");
        await repository.RunGitAsync("checkout", "main");
        repository.WriteFile("local.txt", "local");
        await repository.CommitAllAsync("local");
        string localCommit = await repository.RunGitAsync("rev-parse", "HEAD");

        await service.SetPullRebaseAsync(ConfigScope.Local, repository.Repository, rebase);
        await service.SetPullFastForwardAsync(ConfigScope.Local, repository.Repository, "true");
        await runner.RunAsync(repository.Repository.Path, ["pull", "--no-edit", ".", "incoming"]);

        string incomingCommit = await repository.RunGitAsync("rev-parse", "incoming");
        if (rebase == "false")
        {
            Assert.AreEqual(localCommit, await repository.RunGitAsync("rev-parse", "HEAD^1"));
            Assert.AreEqual(incomingCommit, await repository.RunGitAsync("rev-parse", "HEAD^2"));
        }
        else
        {
            Assert.AreNotEqual(localCommit, await repository.RunGitAsync("rev-parse", "HEAD"));
            Assert.AreEqual(incomingCommit, await repository.RunGitAsync("rev-parse", "HEAD^"));
        }
        Assert.IsTrue(repository.FileExists("local.txt"));
        Assert.IsTrue(repository.FileExists("incoming.txt"));
    }

    [TestMethod]
    public async Task MissingIdentity_DoesNotPreventSavingOtherSettings()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        GitConfigService service = new();
        await service.UnsetUserNameAsync(ConfigScope.Local, repository.Repository);
        await service.UnsetUserEmailAsync(ConfigScope.Local, repository.Repository);
        await service.UnsetUserNameAsync(ConfigScope.Local, repository.Repository);
        await service.UnsetUserEmailAsync(ConfigScope.Local, repository.Repository);
    }

    [TestMethod]
    public async Task PullSettings_AreIndependentAcrossScopesAndCanBeRemoved()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        using TemporaryDirectory configurationDirectory = new();
        IsolatedConfigRunner runner = new(configurationDirectory.CreateFile("global.gitconfig"));
        GitConfigService service = new(runner);

        Assert.AreEqual(new GitPullSettings(null, null), await service.GetPullSettingsAsync(ConfigScope.Global, null));
        await service.SetPullRebaseAsync(ConfigScope.Global, null, "false");
        await service.SetPullFastForwardAsync(ConfigScope.Global, null, "true");
        Assert.AreEqual(new GitPullSettings("false", "true"), await service.GetPullSettingsAsync(ConfigScope.Global, null));
        Assert.AreEqual(new GitPullSettings(null, null), await service.GetPullSettingsAsync(ConfigScope.Local, repository.Repository));

        await service.SetPullRebaseAsync(ConfigScope.Local, repository.Repository, "true");
        await service.SetPullFastForwardAsync(ConfigScope.Local, repository.Repository, "only");
        Assert.AreEqual(new GitPullSettings("true", "only"), await service.GetPullSettingsAsync(ConfigScope.Local, repository.Repository));

        await service.SetPullRebaseAsync(ConfigScope.Local, repository.Repository, null);
        await service.SetPullFastForwardAsync(ConfigScope.Local, repository.Repository, null);
        await service.SetPullFastForwardAsync(ConfigScope.Local, repository.Repository, null);
        Assert.AreEqual(new GitPullSettings(null, null), await service.GetPullSettingsAsync(ConfigScope.Local, repository.Repository));
        GitCommandResult inherited = await runner.RunAsync(repository.Repository.Path, ["config", "--get", "pull.rebase"]);
        Assert.AreEqual("false", inherited.StandardOutput.Trim());
        Assert.AreEqual(new GitPullSettings("false", "true"), await service.GetPullSettingsAsync(ConfigScope.Global, null));

        await service.SetPullRebaseAsync(ConfigScope.Global, null, null);
        await service.SetPullFastForwardAsync(ConfigScope.Global, null, null);
        Assert.AreEqual(new GitPullSettings(null, null), await service.GetPullSettingsAsync(ConfigScope.Global, null));
    }

    [TestMethod]
    public async Task PullSettings_EmptyAndRepeatedValuesAreNotTreatedAsAbsent()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        GitConfigService service = new();
        await repository.RunGitAsync("config", "--local", "pull.rebase", "");
        Assert.AreEqual(new GitPullSettings("", null), await service.GetPullSettingsAsync(ConfigScope.Local, repository.Repository));
        await repository.RunGitAsync("config", "--local", "--add", "pull.rebase", "merges");
        await service.SetPullRebaseAsync(ConfigScope.Local, repository.Repository, "false");
        Assert.AreEqual("false", await repository.RunGitAsync("config", "--local", "--get-all", "pull.rebase"));
    }

    [TestMethod]
    [DataRow("false", "true")]
    [DataRow("true", "false")]
    [DataRow("merges", "only")]
    [DataRow("interactive", "true")]
    public async Task PullSettings_SupportedValuesRoundTrip(string rebase, string fastForward)
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        GitConfigService service = new();
        await service.SetPullRebaseAsync(ConfigScope.Local, repository.Repository, rebase);
        await service.SetPullFastForwardAsync(ConfigScope.Local, repository.Repository, fastForward);
        Assert.AreEqual(new GitPullSettings(rebase, fastForward), await service.GetPullSettingsAsync(ConfigScope.Local, repository.Repository));
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(128)]
    public async Task PullSettings_ReadAndWriteFailuresAreSurfaced(int exitCode)
    {
        RecordingRunner runner = new() { Result = new GitCommandResult(exitCode, "", "config failed") };
        GitConfigService service = new(runner);
        await Assert.ThrowsAsync<GitCommandException>(() => service.GetPullSettingsAsync(ConfigScope.Global, null));
        await Assert.ThrowsAsync<GitCommandException>(() => service.SetPullRebaseAsync(ConfigScope.Global, null, "false"));
        await Assert.ThrowsAsync<GitCommandException>(() => service.SetPullFastForwardAsync(ConfigScope.Global, null, null));
        Assert.IsTrue(runner.Calls.All(call => call.Options!.UseDefaultWorkingDirectory));
    }

    [TestMethod]
    public async Task PullSettings_RejectInvalidInputBeforeRunningGit()
    {
        RecordingRunner runner = new();
        GitConfigService service = new(runner);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetPullRebaseAsync(ConfigScope.Global, null, "invalid"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetPullFastForwardAsync(ConfigScope.Global, null, "invalid"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.GetPullSettingsAsync(ConfigScope.Local, null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetPullSettingsAsync(ConfigScope.None, null));
        Assert.IsEmpty(runner.Calls);
    }

    private sealed class IsolatedConfigRunner(string globalConfigPath) : IGitCommandRunner
    {
        public Task<GitCommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments,
            GitCommandOptions? options = null, CancellationToken cancellationToken = default)
        {
            GitCommandOptions isolatedOptions = (options ?? new GitCommandOptions()) with
            {
                EnvironmentVariables = new Dictionary<string, string>
                {
                    ["GIT_CONFIG_GLOBAL"] = globalConfigPath,
                    ["GIT_CONFIG_NOSYSTEM"] = "1"
                }
            };
            return new GitCommandRunner().RunAsync(workingDirectory, arguments, isolatedOptions, cancellationToken);
        }
    }

    private sealed class RecordingRunner : IGitCommandRunner
    {
        public GitCommandResult Result { get; init; } = new(0, "", "");
        public List<(IReadOnlyList<string> Arguments, GitCommandOptions? Options)> Calls { get; } = [];

        public Task<GitCommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments,
            GitCommandOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add((arguments, options));
            return Task.FromResult(Result);
        }
    }

}
