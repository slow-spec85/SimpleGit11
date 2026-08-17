using System.Collections.Generic;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitStashService
{
    Task<IReadOnlyList<GitStash>> GetStashesAsync(RepositoryInfo repository);

    Task<string> CreateStashAsync(RepositoryInfo repository);

    Task<string> ApplyStashAsync(RepositoryInfo repository, GitStash stash);

    Task<string> PopStashAsync(RepositoryInfo repository, GitStash stash);

    Task<string> DropStashAsync(RepositoryInfo repository, GitStash stash);

    Task<string> ClearStashesAsync(RepositoryInfo repository);
}
