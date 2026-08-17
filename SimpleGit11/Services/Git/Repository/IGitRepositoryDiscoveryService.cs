using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitRepositoryDiscoveryService
{
    RepositoryInfo? TryOpenRepository(string path);
}
