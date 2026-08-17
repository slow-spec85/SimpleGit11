using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Services;

public sealed class GitArchiveService : IGitArchiveService
{
    private readonly IGitCommandRunner _commandRunner;

    public GitArchiveService(IGitCommandRunner? commandRunner = null)
    {
        _commandRunner = commandRunner ?? new GitCommandRunner();
    }

    public async Task CreateAsync(
        RepositoryInfo repository,
        GitArchiveRequest request,
        CancellationToken cancellationToken)
    {
        string outputPath = Path.GetFullPath(request.OutputPath);
        string? outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            throw new DirectoryNotFoundException(outputDirectory);
        }

        string rootDirectoryName = NormalizeRootDirectoryName(request.RootDirectoryName);
        string temporaryPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");

        List<string> arguments =
        [
            "archive",
            $"--format={GetFormatArgument(request.Format)}",
            $"--output={temporaryPath}"
        ];
        if (!string.IsNullOrWhiteSpace(rootDirectoryName))
        {
            arguments.Add($"--prefix={rootDirectoryName}/");
        }

        arguments.Add(request.Revision);

        try
        {
            await _commandRunner.RunAsync(
                repository.Path,
                arguments,
                cancellationToken: cancellationToken);
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static string GetFormatArgument(GitArchiveFormat format)
    {
        return format switch
        {
            GitArchiveFormat.Zip => "zip",
            GitArchiveFormat.TarGZip => "tar.gz",
            GitArchiveFormat.Tar => "tar",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private static string NormalizeRootDirectoryName(string rootDirectoryName)
    {
        string normalized = rootDirectoryName.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "";
        }

        if (Path.IsPathRooted(normalized)
            || normalized.Contains(':', StringComparison.Ordinal)
            || normalized.Split('/').Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException(
                "The archive root directory must be a safe relative path.",
                nameof(rootDirectoryName));
        }

        return normalized;
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
