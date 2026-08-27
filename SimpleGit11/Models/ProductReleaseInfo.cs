using System;

namespace SimpleGit11.Models;

public sealed record ProductReleaseInfo(
    string Version,
    Uri Uri,
    bool IsPrerelease);
