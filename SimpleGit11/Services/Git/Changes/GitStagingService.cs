using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services;

public sealed class GitStagingService : IGitStagingService
{
    private readonly IGitCommandRunner _commandRunner;

    public GitStagingService(IGitCommandRunner? commandRunner = null)
    {
        _commandRunner = commandRunner ?? new GitCommandRunner();
    }

    public Task StageAsync(RepositoryInfo repository, GitChangedFile changedFile)
    {
        return RunGitAsync(repository, ["add", "--", changedFile.Path]);
    }

    public Task UnstageAsync(RepositoryInfo repository, GitChangedFile changedFile)
    {
        return RunGitAsync(repository, ["restore", "--staged", "--", changedFile.Path]);
    }

    public Task StageAllAsync(RepositoryInfo repository)
    {
        return RunGitAsync(repository, ["add", "--all"]);
    }

    public Task UnstageAllAsync(RepositoryInfo repository)
    {
        return RunGitAsync(repository, ["restore", "--staged", "--", "."]);
    }

    private async Task RunGitAsync(RepositoryInfo repository, string[] arguments)
    {
        _ = await _commandRunner.RunAsync(repository.Path, arguments);
    }
}
