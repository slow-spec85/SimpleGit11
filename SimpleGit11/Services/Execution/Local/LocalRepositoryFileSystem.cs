using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Services.Execution.Local;

public sealed class LocalRepositoryFileSystem : IRepositoryFileSystem
{
    public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(path));
    }

    public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Directory.Exists(path));
    }

    public Task<IReadOnlyList<string>> EnumerateDirectoriesAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult<IReadOnlyList<string>>(
                Directory.EnumerateDirectories(path).ToArray());
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
        catch (IOException)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
    }

    public Task<bool> IsSymbolicLinkAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileSystemInfo? entry = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : File.Exists(path)
                ? new FileInfo(path)
                : null;
        return Task.FromResult(
            entry?.Attributes.HasFlag(FileAttributes.ReparsePoint) == true);
    }

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        return File.ReadAllBytesAsync(path, cancellationToken);
    }

    public async Task WriteAllBytesAtomicAsync(
        string path,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException($"The parent directory for '{path}' could not be determined.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<RepositoryFileMetadata?> GetMetadataAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path))
        {
            FileInfo file = new(path);
            return Task.FromResult<RepositoryFileMetadata?>(new RepositoryFileMetadata(
                false,
                file.Length,
                file.LastWriteTimeUtc));
        }

        if (Directory.Exists(path))
        {
            DirectoryInfo directory = new(path);
            return Task.FromResult<RepositoryFileMetadata?>(new RepositoryFileMetadata(
                true,
                0,
                directory.LastWriteTimeUtc));
        }

        return Task.FromResult<RepositoryFileMetadata?>(null);
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(path);
        return Task.CompletedTask;
    }
}
