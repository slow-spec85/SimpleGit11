using System.Collections.Generic;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IRecentRepositoriesService
{
    IReadOnlyList<RepositoryInfo> Load();

    IReadOnlyList<RepositoryInfo> Add(RepositoryInfo repository);

    IReadOnlyList<RepositoryInfo> Remove(RepositoryInfo repository);
}
