using SimpleGit11.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Services;

public interface IGitRevisionService
{
    Task<IReadOnlyList<GitRevisionSuggestion>> GetSuggestionsAsync(
        RepositoryInfo repository,
        GitRevisionKind kind,
        CancellationToken cancellationToken);

    Task<GitResolvedRevision> ResolveAsync(
        RepositoryInfo repository,
        GitRevisionKind kind,
        string value,
        CancellationToken cancellationToken);
}
