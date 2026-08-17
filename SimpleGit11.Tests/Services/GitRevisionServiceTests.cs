using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Tests.TestInfrastructure;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitRevisionServiceTests
{
    [TestMethod]
    public async Task GetSuggestionsAsync_ReturnsBranchesTagsAndCommits()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("file.txt", "content");
        await repository.CommitAllAsync("Initial commit");
        await repository.RunGitAsync("branch", "feature");
        await repository.RunGitAsync("tag", "v1.0");
        GitRevisionService service = new();

        IReadOnlyList<GitRevisionSuggestion> branches = await service.GetSuggestionsAsync(
            repository.Repository,
            GitRevisionKind.Branch,
            CancellationToken.None);
        IReadOnlyList<GitRevisionSuggestion> tags = await service.GetSuggestionsAsync(
            repository.Repository,
            GitRevisionKind.Tag,
            CancellationToken.None);
        IReadOnlyList<GitRevisionSuggestion> commits = await service.GetSuggestionsAsync(
            repository.Repository,
            GitRevisionKind.Commit,
            CancellationToken.None);

        Assert.IsTrue(branches.Any(item => item.Value == "feature" && !item.IsRemote));
        Assert.IsTrue(tags.Any(item => item.Value == "v1.0"));
        Assert.IsTrue(commits.Any(item => item.DisplayName.Contains("Initial commit")));
        Assert.IsTrue(commits.All(item => !string.IsNullOrWhiteSpace(item.ShortHash)));
    }

    [TestMethod]
    public async Task ResolveAsync_ReturnsFullAndGitShortHashes()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        await repository.RunGitAsync("commit", "--allow-empty", "-m", "Initial commit");
        await repository.RunGitAsync("branch", "feature");
        string expectedHash = await repository.RunGitAsync("rev-parse", "HEAD");
        string expectedShortHash = await repository.RunGitAsync("rev-parse", "--short", "HEAD");
        GitRevisionService service = new();

        GitResolvedRevision resolved = await service.ResolveAsync(
            repository.Repository,
            GitRevisionKind.Branch,
            "feature",
            CancellationToken.None);

        Assert.AreEqual(expectedHash, resolved.CommitHash);
        Assert.AreEqual(expectedShortHash, resolved.ShortHash);
    }
}
