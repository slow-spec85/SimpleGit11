using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitRemoteHttpsAuthenticationTests
{
    [TestMethod]
    public async Task CheckAccessAsync_HttpsUrl_PassesUrlToAuthenticationPipeline()
    {
        RecordingGitCommandRunner runner = new();
        GitRemoteService service = new(
            new GitTagService(runner),
            new GitConfigService(runner),
            runner);
        RepositoryInfo repository = new("/repo", "repo", "main");

        await service.CheckAccessAsync(
            repository,
            "https://example.test/team/project.git");

        CollectionAssert.AreEqual(
            new[] { "ls-remote", "https://example.test/team/project.git" },
            new List<string>(runner.Arguments));
        Assert.AreEqual(
            "https://example.test/team/project.git",
            runner.Options?.HttpAuthentication?.Url);
    }

    private sealed class RecordingGitCommandRunner : IGitCommandRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public GitCommandOptions? Options { get; private set; }

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Arguments = arguments;
            Options = options;
            return Task.FromResult(new GitCommandResult(0, "", ""));
        }
    }
}
