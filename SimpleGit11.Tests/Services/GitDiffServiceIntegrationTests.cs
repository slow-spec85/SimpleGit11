using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public sealed class GitDiffServiceIntegrationTests
{
    [TestMethod]
    [DataRow(DiffLineKind.Added)]
    [DataRow(DiffLineKind.Removed)]
    public async Task RevertChangeAsync_DiffChangedLine_RestoresItsBlock(DiffLineKind selectedKind)
    {
        await using TemporaryGitRepository repository =
            await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("file.txt", "before\r\nold value\r\nafter\r\n");
        await repository.CommitAllAsync();
        repository.WriteFile("file.txt", "before\r\nnew value\r\nafter\r\n");

        SettingsService settingsService = new(new InMemoryLocalSettingsStore());
        GitDiffService service = new(settingsService);
        GitChangedFile changedFile = new(
            "file.txt",
            "Modified",
            state: GitChangeState.Unstaged);
        DiffResult diff = await service.GetDiffAsync(repository.Repository, changedFile);
        DiffLine selectedLine = diff.Lines.Single(line => line.Kind == selectedKind);

        await service.RevertChangeAsync(
            repository.Repository,
            changedFile,
            selectedLine.SourceLineNumber!.Value);

        Assert.AreEqual("before\r\nold value\r\nafter\r\n", repository.ReadFile("file.txt"));
    }

    [TestMethod]
    public async Task GetCommitFileTextAsync_Submodule_DoesNotReadGitlinkAsFile()
    {
        await using TemporaryGitRepository repository =
            await TemporaryGitRepository.CreateAsync();
        SettingsService settingsService = new(new InMemoryLocalSettingsStore());
        GitDiffService service = new(settingsService);
        GitCommit missingCommit = new(
            "0000000000000000000000000000000000000000",
            "0000000",
            "",
            "",
            null,
            "",
            "");
        GitChangedFile submodule = new(
            "External/Library",
            "Modified",
            isSubmodule: true);

        string text = await service.GetCommitFileTextAsync(
            repository.Repository,
            missingCommit,
            submodule);

        Assert.AreEqual("", text);
    }

    [TestMethod]
    public async Task SearchFilesAsync_UsesIndexAndWorkingTreeVersionsAndIncludesNewFiles()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("sample.txt", "unchanged marker\nold value\n");
        await repository.CommitAllAsync();
        repository.WriteFile("sample.txt", "unchanged marker\nindex value\n");
        await repository.RunGitAsync("add", "--", "sample.txt");
        repository.WriteFile("sample.txt", "unchanged marker\nworking value\n");
        repository.WriteFile("новая папка/[new].txt", "index value index value\n");

        GitStatusSnapshot status = await new GitStatusService().GetStatusAsync(repository.Repository);
        GitChangedFile[] files = [.. status.StagedChanges, .. status.UnstagedChanges];
        GitDiffService service = new(new SettingsService(new InMemoryLocalSettingsStore()));
        IReadOnlyList<GitFileSearchResult> full = await service.SearchFilesAsync(
            repository.Repository, null, files, "index value", false, CancellationToken.None);
        IReadOnlyList<GitFileSearchResult> diff = await service.SearchFilesAsync(
            repository.Repository, null, files, "index value", true, CancellationToken.None);

        Assert.HasCount(3, full);
        Assert.IsTrue(full[0].File.IsStaged);
        Assert.AreEqual(2, full[0].Matches.Single().Line);
        GitFileSearchResult newFile = full.Single(result => result.File.IsUntracked);
        Assert.AreEqual("новая папка/[new].txt", newFile.File.Path);
        CollectionAssert.AreEqual(new[] { 0, 12 }, newFile.Matches.Select(match => match.Column).ToArray());
        Assert.HasCount(3, diff);
        GitFileSearchResult workingTree = diff.Single(result => !result.File.IsStaged && !result.File.IsUntracked);
        DiffResult patch = await service.GetDiffAsync(repository.Repository, workingTree.File);
        GitFileSearchMatch removed = workingTree.Matches.Single();
        Assert.IsTrue(removed.IsDiffRow);
        Assert.AreEqual(DiffLineKind.Removed, patch.Lines[removed.Line].Kind);
        Assert.AreEqual(removed, full.Single(result => result.File == workingTree.File).Matches.Single());

        GitFileSearchResult stagedRemoval = (await service.SearchFilesAsync(
            repository.Repository, null, files, "old value", false, CancellationToken.None)).Single();
        Assert.IsTrue(stagedRemoval.File.IsStaged);
        Assert.IsTrue(stagedRemoval.Matches.Single().IsDiffRow);
    }

    [TestMethod]
    [DataRow("needle", false, 4)]
    [DataRow("needle", true, 3)]
    [DataRow("old", false, 1)]
    public async Task SearchFilesAndHistory_IncludeRemovedLinesFromModifiedFiles(
        string query, bool changedLinesOnly, int expectedCount)
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("file.txt", "context needle\nold needle needle\n");
        await repository.CommitAllAsync();
        repository.WriteFile("file.txt", "context needle\nnew needle\n");
        await repository.CommitAllAsync();

        GitHistoryService history = new();
        GitCommit commit = await history.GetLastCommitAsync(repository.Repository);
        IReadOnlyList<GitChangedFile> files = await history.GetChangedFilesAsync(repository.Repository, commit);
        GitDiffService service = new(new SettingsService(new InMemoryLocalSettingsStore()));
        GitFileSearchResult selectedCommitResult = (await service.SearchFilesAsync(
            repository.Repository, commit, files, query, changedLinesOnly, CancellationToken.None)).Single();
        GitFileSearchResult historyResult = (await service.SearchHistoryAsync(
            repository.Repository, query, changedLinesOnly, CancellationToken.None))
            .Single(result => result.Commit?.Hash == commit.Hash);

        CollectionAssert.AreEqual(selectedCommitResult.Matches.ToArray(), historyResult.Matches.ToArray());
        Assert.HasCount(expectedCount, selectedCommitResult.Matches);
        DiffResult patch = await service.GetCommitDiffAsync(repository.Repository, commit, files.Single());
        GitFileSearchMatch[] removed = selectedCommitResult.Matches.Where(match =>
            match.IsDiffRow && patch.Lines[match.Line].Kind == DiffLineKind.Removed).ToArray();
        Assert.HasCount(query == "old" ? 1 : 2, removed);
        if (!changedLinesOnly && query == "needle")
        {
            Assert.AreEqual(1, selectedCommitResult.Matches[0].Line);
            Assert.AreEqual(removed[0], selectedCommitResult.Matches[1]);
            Assert.AreEqual(removed[1], selectedCommitResult.Matches[2]);
            Assert.AreEqual(2, selectedCommitResult.Matches[3].Line);
        }
    }

    [TestMethod]
    public async Task SearchHistoryAsync_SearchesOnlyTouchedVersionsAndFindsLiteralAddedAndRemovedLines()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        const string path = "текст [example].txt";
        repository.WriteFile(path, "TOKEN [x]\noriginal\n");
        repository.WriteFile("unmodified.txt", "TOKEN [x]\n");
        await repository.CommitAllAsync("initial");
        repository.WriteFile(path, "TOKEN [x]\nupdated\n");
        await repository.CommitAllAsync("other line");
        await repository.RunGitAsync("rm", "--", path);
        await repository.CommitAllAsync("delete");
        await repository.RunGitAsync("switch", "-c", "other");
        repository.WriteFile("other.txt", "TOKEN [x]\n");
        await repository.CommitAllAsync("outside current branch");
        await repository.RunGitAsync("switch", "main");

        GitDiffService service = new(new SettingsService(new InMemoryLocalSettingsStore()));
        IReadOnlyList<GitFileSearchResult> full = await service.SearchHistoryAsync(
            repository.Repository, "token [x]", false, CancellationToken.None);
        IReadOnlyList<GitFileSearchResult> diff = await service.SearchHistoryAsync(
            repository.Repository, "token [x]", true, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "delete", "other line", "initial", "initial" },
            full.Select(result => result.Commit?.Title).ToArray());
        CollectionAssert.AreEqual(new[] { "delete", "initial", "initial" },
            diff.Select(result => result.Commit?.Title).ToArray());
        Assert.AreEqual(path, full[0].File.Path);
        Assert.AreEqual("Deleted", full[0].File.Status);
        Assert.AreEqual(1, full[0].Matches.Single().Line);
        Assert.IsTrue(diff.All(result => result.Matches.Single().IsDiffRow));
    }

    [TestMethod]
    [DataRow("refs/heads/topic", false)]
    [DataRow("refs/heads/topic", true)]
    [DataRow("refs/remotes/origin/topic", false)]
    [DataRow("refs/remotes/origin/topic", true)]
    public async Task SearchHistoryAsync_SearchesRequestedReferenceWithoutSwitchingBranches(
        string reference, bool changedLinesOnly)
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("shared.txt", "needle\n");
        await repository.CommitAllAsync("shared");
        await repository.RunGitAsync("switch", "-c", "topic");
        repository.WriteFile("topic.txt", "needle\n");
        await repository.CommitAllAsync("topic change");
        await repository.RunGitAsync("update-ref", "refs/remotes/origin/topic", "refs/heads/topic");
        await repository.RunGitAsync("switch", "main");
        repository.WriteFile("main.txt", "needle\n");
        await repository.CommitAllAsync("main change");

        GitDiffService service = new(new SettingsService(new InMemoryLocalSettingsStore()));
        IReadOnlyList<GitFileSearchResult> results = await service.SearchHistoryAsync(
            repository.Repository, "needle", changedLinesOnly, CancellationToken.None, reference);

        CollectionAssert.AreEqual(new[] { "topic.txt", "shared.txt" },
            results.Select(result => result.File.Path).ToArray());
        Assert.AreEqual("main", await repository.RunGitAsync("branch", "--show-current"));
    }

    [TestMethod]
    public async Task SearchHistoryAsync_PureRenameMatchesFullFileAndPreservesPath()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("old.txt", "needle\n");
        await repository.CommitAllAsync("initial");
        await repository.RunGitAsync("mv", "--", "old.txt", "renamed [file].txt");
        await repository.CommitAllAsync("rename");

        GitDiffService service = new(new SettingsService(new InMemoryLocalSettingsStore()));
        IReadOnlyList<GitFileSearchResult> full = await service.SearchHistoryAsync(
            repository.Repository, "needle", false, CancellationToken.None);
        IReadOnlyList<GitFileSearchResult> diff = await service.SearchHistoryAsync(
            repository.Repository, "needle", true, CancellationToken.None);

        Assert.HasCount(2, full);
        Assert.AreEqual("renamed [file].txt", full[0].File.Path);
        Assert.AreEqual("old.txt", full[0].File.PreviousPath);
        Assert.AreEqual("initial", diff.Single().Commit?.Title);

        IReadOnlyList<GitChangedFile> changedFiles = await new GitHistoryService().GetChangedFilesAsync(
            repository.Repository, full[0].Commit!);
        Assert.AreEqual("old.txt", changedFiles.Single().PreviousPath);
    }

    [TestMethod]
    public async Task SearchHistoryAsync_IncludesMergedCommitsAndUsesTheFirstParentForMergeDiffs()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("base.txt", "base\n");
        await repository.CommitAllAsync("initial");
        await repository.RunGitAsync("switch", "-c", "topic");
        repository.WriteFile("feature.txt", "feature text\n");
        await repository.CommitAllAsync("topic change");
        await repository.RunGitAsync("switch", "main");
        repository.WriteFile("base.txt", "main\n");
        await repository.CommitAllAsync("main change");
        await repository.RunGitAsync("merge", "--no-ff", "topic", "-m", "merge topic");

        GitDiffService service = new(new SettingsService(new InMemoryLocalSettingsStore()));
        IReadOnlyList<GitFileSearchResult> results = await service.SearchHistoryAsync(
            repository.Repository, "feature text", true, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "merge topic", "topic change" },
            results.Select(result => result.Commit?.Title).ToArray());
        IReadOnlyList<GitChangedFile> mergeFiles = await new GitHistoryService().GetChangedFilesAsync(
            repository.Repository, results[0].Commit!);
        Assert.AreEqual("feature.txt", mergeFiles.Single().Path);
        Assert.AreEqual(1, mergeFiles.Single().Stat.AddedLines);
    }

    private sealed class InMemoryLocalSettingsStore : ILocalSettingsStore
    {
        private readonly Dictionary<string, string> _values = [];

        public string? GetString(string key)
        {
            return _values.TryGetValue(key, out string? value) ? value : null;
        }

        public void SetString(string key, string value)
        {
            _values[key] = value;
        }
    }
}
