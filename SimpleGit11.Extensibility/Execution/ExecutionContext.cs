using System;

namespace SimpleGit11.Services.Execution;

public sealed record ExecutionContext(
    Guid Id,
    long Version,
    string ProviderId,
    string? ConnectionProfileId,
    IExecutionRuntime Runtime)
{
    public string DisplayMachineName => Runtime.DisplayMachineName;

    public bool IsLocal => Runtime.Capabilities.HasFlag(ExecutionCapabilities.LocalMachine);
}
