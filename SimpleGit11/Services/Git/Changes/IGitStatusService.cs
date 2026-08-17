using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitStatusService
{
    Task<GitStatusSnapshot> GetStatusAsync(RepositoryInfo repository);

    Task<GitOperationState> GetOperationStateAsync(RepositoryInfo repository);
}
