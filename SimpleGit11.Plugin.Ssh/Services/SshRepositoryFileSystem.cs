using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Plugin.Ssh.Services;

public sealed class SshRepositoryFileSystem : IRepositoryFileSystem, IRepositoryFileTransfer, IAsyncDisposable
{
    private readonly SftpClient _client;
    private readonly SshConnectionMonitor _connectionMonitor;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    private SshRepositoryFileSystem(
        SftpClient client,
        SshConnectionMonitor connectionMonitor)
    {
        _client = client;
        _connectionMonitor = connectionMonitor;
        _client.ErrorOccurred += Client_ErrorOccurred;
    }

    public static async Task<SshRepositoryFileSystem> ConnectAsync(
        SshConnectionSettings settings,
        SshConnectionMonitor connectionMonitor,
        CancellationToken cancellationToken)
    {
        SftpClient client = new(SshCommandSession.CreateConnectionInfo(settings));
        try
        {
            await SshCommandSession.ConnectAndVerifyHostKeyAsync(client, settings, cancellationToken);
            return new SshRepositoryFileSystem(client, connectionMonitor);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        SftpFileAttributes? attributes = await GetAttributesOrDefaultAsync(path, cancellationToken);
        return attributes is not null && !attributes.IsDirectory;
    }

    public async Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        SftpFileAttributes? attributes = await GetAttributesOrDefaultAsync(path, cancellationToken);
        return attributes?.IsDirectory == true;
    }

    public async Task<IReadOnlyList<string>> EnumerateDirectoriesAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            List<string> directories = [];
            try
            {
                await foreach (ISftpFile entry in _client.ListDirectoryAsync(path, cancellationToken))
                {
                    if (entry.IsDirectory && entry.Name is not "." and not "..")
                    {
                        directories.Add(entry.FullName);
                    }
                }
            }
            catch (SftpPathNotFoundException)
            {
                return [];
            }
            catch (SftpPermissionDeniedException)
            {
                return [];
            }

            return directories;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<bool> IsSymbolicLinkAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            try
            {
                ISftpFile entry = await _client.GetAsync(path, cancellationToken);
                return entry.IsSymbolicLink;
            }
            catch (SftpPathNotFoundException)
            {
                return false;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            using MemoryStream content = new();
            await _client.DownloadFileAsync(path, content, cancellationToken);
            return content.ToArray();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task WriteAllBytesAtomicAsync(
        string path,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            using MemoryStream stream = new(content, writable: false);
            await _client.UploadFileAsync(stream, temporaryPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _client.RenameFile(temporaryPath, path, isPosix: true);
            }
            catch (SshException exception) when (
                exception is not SftpPermissionDeniedException && _client.IsConnected)
            {
                SftpAtomicFileReplacement.ReplaceWithoutPosixExtension(
                    temporaryPath,
                    path,
                    candidate => _client.Exists(candidate),
                    (source, destination) => _client.RenameFile(source, destination),
                    candidate => _client.DeleteFile(candidate));
            }
        }
        finally
        {
            try
            {
                if (await _client.ExistsAsync(temporaryPath, CancellationToken.None))
                {
                    await _client.DeleteFileAsync(temporaryPath, CancellationToken.None);
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }
    }

    public async Task<RepositoryFileMetadata?> GetMetadataAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        SftpFileAttributes? attributes = await GetAttributesOrDefaultAsync(path, cancellationToken);
        return attributes is null
            ? null
            : new RepositoryFileMetadata(
                attributes.IsDirectory,
                attributes.Size,
                attributes.LastWriteTimeUtc);
    }

    public async Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            await _client.DeleteFileAsync(path, cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task DownloadAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            await using FileStream destination = new(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous);
            await _client.DownloadFileAsync(sourcePath, destination, cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _operationLock.WaitAsync();
        try
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }

            _client.Dispose();
        }
        finally
        {
            _operationLock.Release();
            _operationLock.Dispose();
        }
    }

    private async Task<SftpFileAttributes?> GetAttributesOrDefaultAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            try
            {
                return await _client.GetAttributesAsync(path, cancellationToken);
            }
            catch (SftpPathNotFoundException)
            {
                return null;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private void Client_ErrorOccurred(object? sender, ExceptionEventArgs e)
    {
        _connectionMonitor.Report(e.Exception);
    }
}
