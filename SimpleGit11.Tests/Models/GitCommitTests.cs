using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;

namespace SimpleGit11.Tests.Models;

[TestClass]
public sealed class GitCommitTests
{
    [TestMethod]
    public void Constructor_WithoutCommitter_UsesAuthorIdentity()
    {
        GitCommit commit = CreateCommit();

        Assert.AreEqual(commit.AuthorName, commit.CommitterName);
        Assert.AreEqual(commit.AuthorEmail, commit.CommitterEmail);
        Assert.IsFalse(commit.HasDistinctCommitter);
    }

    [TestMethod]
    public void DistinctCommitter_ExposesFormattedIdentity()
    {
        GitCommit commit = CreateCommit(
            committerName: "Committer Name",
            committerEmail: "committer@example.invalid");

        Assert.AreEqual("Committer Name <committer@example.invalid>", commit.DisplayCommitter);
        Assert.IsTrue(commit.HasDistinctCommitter);
    }

    private static GitCommit CreateCommit(
        string? committerName = null,
        string? committerEmail = null) => new(
        "hash",
        "short-hash",
        "Author Name",
        "author@example.invalid",
        null,
        "Title",
        "Message",
        committerName: committerName,
        committerEmail: committerEmail);
}
