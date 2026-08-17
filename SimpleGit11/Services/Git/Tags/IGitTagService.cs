using System.Collections.Generic;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public interface IGitTagService
{
    Task<IReadOnlyList<GitTag>> GetLocalTagsAsync(RepositoryInfo repository);

    Task<string?> GetHeadCommitHashAsync(RepositoryInfo repository);

    Task CreateTagAsync(RepositoryInfo repository, TagCreationRequest request);

    Task CheckoutTagAsync(RepositoryInfo repository, GitTag tag);

    Task DeleteTagAsync(RepositoryInfo repository, GitTag tag);
}
