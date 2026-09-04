using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Services.Execution;

public interface IRepositoryFileSystem
{
    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default);

    Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> EnumerateDirectoriesAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<bool> IsSymbolicLinkAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);

    Task WriteAllBytesAtomicAsync(
        string path,
        byte[] content,
        CancellationToken cancellationToken = default);

    Task<RepositoryFileMetadata?> GetMetadataAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}
