using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitIgnoreService
{
    Task AddAsync(RepositoryInfo repository, GitChangedFile changedFile);
}
