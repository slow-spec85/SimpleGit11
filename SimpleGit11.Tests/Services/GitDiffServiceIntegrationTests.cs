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

        SettingsService settingsService = new(
            new InMemoryLocalSettingsStore(),
            new TestProductInfoService());
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
