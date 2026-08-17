using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitStagingService
{
    Task StageAsync(RepositoryInfo repository, GitChangedFile changedFile);

    Task UnstageAsync(RepositoryInfo repository, GitChangedFile changedFile);

    Task StageAllAsync(RepositoryInfo repository);

    Task UnstageAllAsync(RepositoryInfo repository);
}
