using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Services;

public sealed class GitIgnoreService : IGitIgnoreService
{
    private readonly IExecutionContextService? _executionContextService;

    public GitIgnoreService(IExecutionContextService? executionContextService = null)
    {
        _executionContextService = executionContextService;
    }

    public async Task AddAsync(RepositoryInfo repository, GitChangedFile changedFile)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(changedFile);

        if (!changedFile.IsUntracked)
        {
            throw new ArgumentException("Only untracked files can be added to .gitignore.", nameof(changedFile));
        }

        if (_executionContextService is null)
        {
            ValidateRepositoryRelativePath(repository.Path, changedFile.Path);
        }
        else
        {
            ValidateContextualRepositoryRelativePath(repository.Path, changedFile.Path);
        }

        string gitIgnorePath = _executionContextService?.Current.Runtime.Paths.Combine(
            repository.Path,
            ".gitignore") ?? Path.Combine(repository.Path, ".gitignore");
        string existingContent;
        if (_executionContextService is null)
        {
            existingContent = File.Exists(gitIgnorePath)
                ? await File.ReadAllTextAsync(gitIgnorePath)
                : "";
        }
        else
        {
            IRepositoryFileSystem files = _executionContextService.Current.Runtime.Files;
            existingContent = await files.FileExistsAsync(gitIgnorePath)
                ? Encoding.UTF8.GetString(await files.ReadAllBytesAsync(gitIgnorePath))
                : "";
        }
        string separator = existingContent.Length > 0
            && !existingContent.EndsWith('\r')
            && !existingContent.EndsWith('\n')
                ? Environment.NewLine
                : "";
        string pattern = CreateRootedLiteralPattern(changedFile.Path);

        string updatedContent = $"{existingContent}{separator}{pattern}{Environment.NewLine}";
        if (_executionContextService is null)
        {
            await File.WriteAllTextAsync(
                gitIgnorePath,
                updatedContent,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        else
        {
            await _executionContextService.Current.Runtime.Files.WriteAllBytesAtomicAsync(
                gitIgnorePath,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(updatedContent));
        }
    }

    private static string CreateRootedLiteralPattern(string path)
    {
        string normalizedPath = path.Replace('\\', '/');
        StringBuilder pattern = new(normalizedPath.Length + 1);
        pattern.Append('/');

        foreach (char character in normalizedPath)
        {
            if (character is '\\' or '*' or '?' or '[' or ']')
            {
                pattern.Append('\\');
            }

            pattern.Append(character);
        }

        return pattern.ToString();
    }

    private static void ValidateRepositoryRelativePath(string repositoryPath, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("The ignored path must be repository-relative.", nameof(relativePath));
        }

        string repositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
        string fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, relativePath));
        string repositoryPrefix = repositoryRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(repositoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The ignored path must be inside the repository.", nameof(relativePath));
        }
    }

    private void ValidateContextualRepositoryRelativePath(string repositoryPath, string relativePath)
    {
        IRepositoryPathService paths = _executionContextService!.Current.Runtime.Paths;
        bool rooted = paths.Style == RepositoryPathStyle.Windows
            ? Path.IsPathRooted(relativePath)
            : relativePath.StartsWith('/');
        if (rooted)
        {
            throw new ArgumentException("The ignored path must be repository-relative.", nameof(relativePath));
        }

        string root = paths.Normalize(repositoryPath).TrimEnd('/', '\\');
        string fullPath = paths.Normalize(paths.Combine(root, relativePath));
        char separator = paths.Style == RepositoryPathStyle.Windows ? '\\' : '/';
        StringComparison comparison = paths.Style == RepositoryPathStyle.Windows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(root + separator, comparison))
        {
            throw new ArgumentException("The ignored path must be inside the repository.", nameof(relativePath));
        }
    }
}
