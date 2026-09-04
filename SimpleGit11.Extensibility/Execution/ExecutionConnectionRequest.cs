using System.Collections.Generic;

namespace SimpleGit11.Services.Execution;

public sealed record ExecutionConnectionRequest(
    string? ProfileId,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyDictionary<string, string>? Secrets = null);
