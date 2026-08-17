using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitCommandRunnerTests
{
    [TestMethod]
    public async Task RunAsync_Success_ReturnsStandardOutput()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        GitCommandRunner runner = new();

        GitCommandResult result = await runner.RunAsync(
            repository.Repository.Path,
            ["rev-parse", "--is-inside-work-tree"]);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("true", result.StandardOutput.Trim());
    }

    [TestMethod]
    public async Task RunAsync_NonZeroExit_ThrowsGitCommandException()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        GitCommandRunner runner = new();

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(() =>
            runner.RunAsync(
                repository.Repository.Path,
                ["rev-parse", "--verify", "missing-reference"]));

        Assert.AreNotEqual(0, exception.ExitCode);
    }

    [TestMethod]
    public async Task RunAsync_ThrowDisabled_ReturnsFailureResult()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        GitCommandRunner runner = new();

        GitCommandResult result = await runner.RunAsync(
            repository.Repository.Path,
            ["rev-parse", "--verify", "missing-reference"],
            new GitCommandOptions(ThrowOnError: false));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreNotEqual(0, result.ExitCode);
    }

    [TestMethod]
    public async Task RunAsync_EnvironmentVariable_IsAvailableToGitProcess()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        GitCommandRunner runner = new();

        GitCommandResult result = await runner.RunAsync(
            repository.Repository.Path,
            ["var", "GIT_EDITOR"],
            new GitCommandOptions(
                EnvironmentVariables: new Dictionary<string, string>
                {
                    ["GIT_EDITOR"] = "true"
                }));

        Assert.AreEqual("true", result.StandardOutput.Trim());
    }
}
