using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class LocalGitCredentialServiceTests
{
    [TestMethod]
    public async Task ApproveAsync_ReturnsAllFilledCredentialAttributesToManager()
    {
        const string filledCredential =
            "protocol=https\n"
            + "host=gitlab.com\n"
            + "username=test-user\n"
            + "password=test-token\n"
            + "oauth_refresh_token=test-refresh-token\n\n";
        RecordingGitCommandRunner runner = new(filledCredential);
        LocalGitCredentialService service = new(runner);

        GitHttpAuthentication? credential = await service.GetAsync(
            "https://gitlab.com/team/project.git");
        Assert.IsNotNull(credential);
        await service.ApproveAsync(credential);

        Assert.HasCount(2, runner.Calls);
        Assert.AreEqual(Path.GetTempPath(), runner.Calls[0].WorkingDirectory);
        Assert.AreEqual(Path.GetTempPath(), runner.Calls[1].WorkingDirectory);
        CollectionAssert.AreEqual(
            new[]
            {
                "-c",
                "credential.credentialStore=dpapi",
                "credential",
                "approve"
            },
            new List<string>(runner.Calls[1].Arguments));
        Assert.AreEqual(filledCredential, runner.Calls[1].Options.StandardInput);
    }

    [TestMethod]
    public async Task GetAsync_FailedCredentialManager_ThrowsSafeGitCommandException()
    {
        RecordingGitCommandRunner runner = new(
            new GitCommandResult(1, "password=must-not-be-reported", "TLS certificate failure"));
        LocalGitCredentialService service = new(runner);

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(() =>
            service.GetAsync("https://gitlab.example.test/team/project.git"));

        Assert.AreEqual(1, exception.ExitCode);
        StringAssert.Contains(exception.Message, "Git Credential Manager failed.");
        StringAssert.Contains(exception.Message, "TLS certificate failure");
        Assert.IsFalse(exception.Message.Contains("must-not-be-reported", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GetAsync_IncompleteCredentialResponse_ThrowsGitCommandException()
    {
        RecordingGitCommandRunner runner = new("protocol=https\nhost=gitlab.example.test\n\n");
        LocalGitCredentialService service = new(runner);

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(() =>
            service.GetAsync("https://gitlab.example.test/team/project.git"));

        Assert.AreEqual(-1, exception.ExitCode);
        StringAssert.Contains(exception.Message, "did not return both a username and a password");
    }

    private sealed class RecordingGitCommandRunner : IGitCommandRunner
    {
        private readonly GitCommandResult _fillResult;

        public RecordingGitCommandRunner(string filledCredential)
            : this(new GitCommandResult(0, filledCredential, ""))
        {
        }

        public RecordingGitCommandRunner(GitCommandResult fillResult)
        {
            _fillResult = fillResult;
        }

        public List<(
            string WorkingDirectory,
            IReadOnlyList<string> Arguments,
            GitCommandOptions Options)> Calls { get; } = [];

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((workingDirectory, arguments, options ?? new GitCommandOptions()));
            GitCommandResult result = arguments[^1] == "fill"
                ? _fillResult
                : new GitCommandResult(0, "", "");
            return Task.FromResult(result);
        }
    }
}
