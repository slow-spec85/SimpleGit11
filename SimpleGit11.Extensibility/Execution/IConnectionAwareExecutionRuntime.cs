using System;

namespace SimpleGit11.Services.Execution;

public interface IConnectionAwareExecutionRuntime
{
    event EventHandler<Exception>? ConnectionLost;
}
