using System.Collections.Generic;

namespace SimpleGit11.Models;

public sealed record GitPushRequest(
    string RemoteName,
    IReadOnlyList<GitPushReferenceUpdate> References,
    GitPushMode Mode);
