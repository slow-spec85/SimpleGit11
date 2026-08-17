using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitRepositoryRepairService
{
    bool IsMissingObjectHistoryError(GitCommandException exception);

    Task<GitRepositoryRepairResult> RepairMissingObjectsAsync(RepositoryInfo repository);
}
