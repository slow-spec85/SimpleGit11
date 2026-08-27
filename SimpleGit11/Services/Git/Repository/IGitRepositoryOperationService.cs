using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitRepositoryOperationService
{
    Task<RepositoryInfo> CreateAsync(string path);

    Task<RepositoryInfo> CloneAsync(
        string parentPath,
        string remoteUrl,
        bool initializeSubmodulesRecursively = false);
}
