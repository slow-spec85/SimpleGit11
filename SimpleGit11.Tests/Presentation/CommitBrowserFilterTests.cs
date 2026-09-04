using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Presentation.Commits;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class CommitBrowserFilterTests
{
    [TestMethod]
    public void Apply_MainlineOnly_FollowsFirstParentChain()
    {
        GitCommit head = CreateCommit("head", parentHashes: ["main-parent", "merged-parent"]);
        GitCommit mergedParent = CreateCommit("merged-parent", message: "merged work");
        GitCommit mainParent = CreateCommit("main-parent", parentHashes: ["root"]);
        GitCommit root = CreateCommit("root");

        IReadOnlyList<GitCommit> result = CommitBrowserFilter.Apply(
            [head, mergedParent, mainParent, root],
            CreateCriteria(mainlineOnly: true));

        CollectionAssert.AreEqual(
            new[] { "head", "main-parent", "root" },
            result.Select(commit => commit.Hash).ToArray());
    }

    [TestMethod]
    public void Apply_MainlineOnlyWithSearch_SearchesOnlyFirstParentChain()
    {
        GitCommit head = CreateCommit("head", parentHashes: ["main-parent", "merged-parent"]);
        GitCommit mergedParent = CreateCommit("merged-parent", message: "needle");
        GitCommit mainParent = CreateCommit("main-parent", message: "ordinary", parentHashes: ["root"]);
        GitCommit root = CreateCommit("root", message: "ordinary");

        IReadOnlyList<GitCommit> result = CommitBrowserFilter.Apply(
            [head, mergedParent, mainParent, root],
            CreateCriteria(mainlineOnly: true, searchText: "needle"));

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Apply_DateRange_IncludesBothSelectedMinutesAndExcludesMissingDates()
    {
        DateTimeOffset selectedDate = LocalDate(2026, 8, 28, 0, 0, 0);
        GitCommit before = CreateCommit("before", authoredAt: LocalDate(2026, 8, 28, 9, 29, 59));
        GitCommit atStart = CreateCommit("at-start", authoredAt: LocalDate(2026, 8, 28, 9, 30, 0));
        GitCommit atEnd = CreateCommit("at-end", authoredAt: LocalDate(2026, 8, 28, 10, 45, 59));
        GitCommit after = CreateCommit("after", authoredAt: LocalDate(2026, 8, 28, 10, 46, 0));
        GitCommit missingDate = CreateCommit("missing-date");

        IReadOnlyList<GitCommit> result = CommitBrowserFilter.Apply(
            [before, atStart, atEnd, after, missingDate],
            CreateCriteria(
                fromDate: selectedDate,
                fromTime: new TimeSpan(9, 30, 0),
                toDate: selectedDate,
                toTime: new TimeSpan(10, 45, 0)));

        CollectionAssert.AreEqual(
            new[] { "at-start", "at-end" },
            result.Select(commit => commit.Hash).ToArray());
    }

    [TestMethod]
    public void Apply_Search_MatchesCommitMetadataIgnoringCaseAndWhitespace()
    {
        GitCommit message = CreateCommit("message", message: "Fix Search Workflow");
        GitCommit author = CreateCommit("author", authorEmail: "developer@example.invalid");
        GitCommit reference = CreateCommit(
            "reference",
            references: [new GitCommitReference("refs/heads/FeatureBranch", GitCommitReferenceKind.LocalBranch)]);
        GitCommit path = CreateCommit("path", changedFilePaths: ["src/Editor/Search.cs"]);

        AssertSingleMatch([message, author, reference, path], " search workflow ", "message");
        AssertSingleMatch([message, author, reference, path], "DEVELOPER@", "author");
        AssertSingleMatch([message, author, reference, path], "featurebranch", "reference");
        AssertSingleMatch([message, author, reference, path], "editor/search", "path");
    }

    [TestMethod]
    public void Apply_ExactFilePathSearch_DoesNotUsePartialPathMatches()
    {
        GitCommit exact = CreateCommit("exact", changedFilePaths: ["src/App.cs"]);
        GitCommit partial = CreateCommit("partial", changedFilePaths: ["tests/src/App.cs.backup"]);

        IReadOnlyList<GitCommit> result = CommitBrowserFilter.Apply(
            [exact, partial],
            CreateCriteria(
                searchText: "src/App.cs",
                exactFilePathSearchText: "src/App.cs"));

        Assert.HasCount(1, result);
        Assert.AreEqual("exact", result[0].Hash);
    }

    [TestMethod]
    public void IsApplied_ReflectsEveryUserVisibleFilter()
    {
        Assert.IsFalse(CreateCriteria().IsApplied);
        Assert.IsTrue(CreateCriteria(mainlineOnly: true).IsApplied);
        Assert.IsTrue(CreateCriteria(fromDate: LocalDate(2026, 8, 28, 0, 0, 0)).IsApplied);
        Assert.IsTrue(CreateCriteria(toDate: LocalDate(2026, 8, 28, 0, 0, 0)).IsApplied);
        Assert.IsTrue(CreateCriteria(searchText: "query").IsApplied);
    }

    private static void AssertSingleMatch(
        IReadOnlyList<GitCommit> commits,
        string query,
        string expectedHash)
    {
        IReadOnlyList<GitCommit> matches = CommitBrowserFilter.Apply(
            commits,
            CreateCriteria(searchText: query));
        Assert.HasCount(1, matches);
        Assert.AreEqual(expectedHash, matches[0].Hash);
    }

    private static CommitFilterCriteria CreateCriteria(
        bool mainlineOnly = false,
        DateTimeOffset? fromDate = null,
        TimeSpan? fromTime = null,
        DateTimeOffset? toDate = null,
        TimeSpan? toTime = null,
        string searchText = "",
        string exactFilePathSearchText = "") => new(
        mainlineOnly,
        fromDate,
        fromTime ?? TimeSpan.Zero,
        toDate,
        toTime ?? new TimeSpan(23, 59, 0),
        searchText,
        exactFilePathSearchText);

    private static DateTimeOffset LocalDate(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second) => new(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local));

    private static GitCommit CreateCommit(
        string hash,
        string message = "message",
        string authorEmail = "author@example.invalid",
        DateTimeOffset? authoredAt = null,
        IReadOnlyList<string>? changedFilePaths = null,
        IReadOnlyList<GitCommitReference>? references = null,
        IReadOnlyList<string>? parentHashes = null) => new(
        hash,
        hash,
        "Author",
        authorEmail,
        authoredAt,
        message,
        message,
        changedFilePaths: changedFilePaths,
        references: references,
        parentHashes: parentHashes);
}
