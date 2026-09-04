using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services.Execution;

public sealed class ContextualGitCommandRunner : IGitCommandRunner
{
    private readonly IExecutionContextService _executionContextService;

    public ContextualGitCommandRunner(IExecutionContextService executionContextService)
    {
        _executionContextService = executionContextService;
    }

    public Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        GitCommandOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ExecutionContext context = _executionContextService.Current;
        return context.Runtime.Git.RunAsync(
            workingDirectory,
            arguments,
            options,
            cancellationToken);
    }
}
