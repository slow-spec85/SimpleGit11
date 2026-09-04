using System;

namespace SimpleGit11.Services.Execution;

public sealed record RepositoryFileMetadata(
    bool IsDirectory,
    long Length,
    DateTimeOffset LastWriteTime);
