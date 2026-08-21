using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitHistoryServiceTests
{
    [TestMethod]
    public async Task GetCommitsPageAsync_ReturnsConsecutivePagesAndHasMoreState()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        for (int index = 1; index <= 5; index++)
        {
            repository.WriteFile($"file-{index}.txt", index.ToString());
            await repository.CommitAllAsync($"commit {index}");
        }

        GitHistoryService service = new();

        GitCommitPage firstPage = await service.GetCommitsPageAsync(repository.Repository, 0, 2);
        GitCommitPage secondPage = await service.GetCommitsPageAsync(repository.Repository, 2, 2);
        GitCommitPage lastPage = await service.GetCommitsPageAsync(repository.Repository, 4, 2);

        Assert.HasCount(2, firstPage.Commits);
        Assert.AreEqual("commit 5", firstPage.Commits[0].Title);
        Assert.AreEqual("commit 4", firstPage.Commits[1].Title);
        Assert.IsTrue(firstPage.Commits[0].ChangedFilePaths.Contains("file-5.txt"));
        Assert.IsTrue(firstPage.HasMore);

        Assert.HasCount(2, secondPage.Commits);
        Assert.AreEqual("commit 3", secondPage.Commits[0].Title);
        Assert.AreEqual("commit 2", secondPage.Commits[1].Title);
        Assert.IsTrue(secondPage.HasMore);

        Assert.HasCount(1, lastPage.Commits);
        Assert.AreEqual("commit 1", lastPage.Commits[0].Title);
        Assert.IsFalse(lastPage.HasMore);
    }

    [TestMethod]
    public async Task GetLastCommitAsync_ReturnsDistinctCommitterIdentity()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        await repository.RunGitAsync(
            "-c",
            "user.name=Committer Name",
            "-c",
            "user.email=committer@example.invalid",
            "commit",
            "--allow-empty",
            "--author=Author Name <author@example.invalid>",
            "-m",
            "distinct identities");
        GitHistoryService service = new();

        GitCommit commit = await service.GetLastCommitAsync(repository.Repository);

        Assert.AreEqual("Author Name", commit.AuthorName);
        Assert.AreEqual("author@example.invalid", commit.AuthorEmail);
        Assert.AreEqual("Committer Name", commit.CommitterName);
        Assert.AreEqual("committer@example.invalid", commit.CommitterEmail);
        Assert.IsTrue(commit.HasDistinctCommitter);
    }
}
