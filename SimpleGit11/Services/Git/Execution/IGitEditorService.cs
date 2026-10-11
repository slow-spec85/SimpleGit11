using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;

namespace SimpleGit11.Services.Git.Execution;

public interface IGitEditorService
{
    Task<GitCommandResult> RunAsync(
        RepositoryInfo repository,
        IReadOnlyList<string> arguments,
        Func<bool, string, Task<string?>>? editText = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? preparedMessages = null);
}
