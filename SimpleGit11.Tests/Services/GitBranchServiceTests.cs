using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitBranchServiceTests
{
    [TestMethod]
    public async Task RebaseAsync_ReplaysCurrentBranchOntoSelectedBranch()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("common.txt", "common");
        await repository.CommitAllAsync("common");
        await repository.RunGitAsync("branch", "feature");
        repository.WriteFile("main.txt", "main");
        await repository.CommitAllAsync("main");
        string mainHead = await repository.RunGitAsync("rev-parse", "HEAD");
        await repository.RunGitAsync("switch", "feature");
        repository.WriteFile("feature.txt", "feature");
        await repository.CommitAllAsync("feature");
        string featureHeadBefore = await repository.RunGitAsync("rev-parse", "HEAD");
        GitBranch selectedBranch = CreateBranch("main");
        GitBranchService service = new();

        GitBranchRebaseResult result = await service.RebaseAsync(
            repository.Repository,
            selectedBranch);

        string featureHeadAfter = await repository.RunGitAsync("rev-parse", "HEAD");
        string featureParent = await repository.RunGitAsync("rev-parse", "HEAD^");
        Assert.IsTrue(result.HeadChanged);
        Assert.AreNotEqual(featureHeadBefore, featureHeadAfter);
        Assert.AreEqual(mainHead, featureParent);
        Assert.AreEqual("feature", await repository.RunGitAsync("branch", "--show-current"));
    }

    [TestMethod]
    public async Task RebaseAsync_AlreadyBasedOnSelectedBranch_ReportsUnchangedHead()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        await repository.RunGitAsync("commit", "--allow-empty", "-m", "initial");
        await repository.RunGitAsync("branch", "base-branch");
        GitBranchService service = new();

        GitBranchRebaseResult result = await service.RebaseAsync(
            repository.Repository,
            CreateBranch("base-branch"));

        Assert.IsFalse(result.HeadChanged);
    }

    [TestMethod]
    public async Task GetRemoteBranchesAsync_ExcludesRemoteHeadSymbolicReference()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        await repository.RunGitAsync("commit", "--allow-empty", "-m", "initial");
        await repository.RunGitAsync("update-ref", "refs/remotes/origin/main", "HEAD");
        await repository.RunGitAsync(
            "symbolic-ref",
            "refs/remotes/origin/HEAD",
            "refs/remotes/origin/main");

        GitBranchService service = new();

        IReadOnlyList<GitBranch> branches =
            await service.GetRemoteBranchesAsync(repository.Repository);

        Assert.HasCount(1, branches);
        Assert.AreEqual("origin/main", branches[0].Name);
    }

    private static GitBranch CreateBranch(string name)
    {
        return new GitBranch(name, false, false, "", "", null);
    }
}
