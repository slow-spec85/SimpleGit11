using System.Collections.Generic;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitRepositorySearchService
{
    string LoadStartPath();

    void SaveStartPath(string path);

    IReadOnlyList<RepositoryInfo> LoadFoundRepositories();

    void SaveFoundRepositories(IReadOnlyList<RepositoryInfo> repositories);

    Task<IReadOnlyList<RepositoryInfo>> SearchAsync(string startPath);
}
