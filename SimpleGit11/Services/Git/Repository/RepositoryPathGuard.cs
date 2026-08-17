using System;
using System.Collections.Generic;
using System.IO;

namespace SimpleGit11.Services;

internal static class RepositoryPathGuard
{
    public static string GetSafeFilePath(string repositoryPath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        string filePath = Path.GetFullPath(Path.Combine(repositoryPath, relativePath));
        if (!IsPathInsideRepository(repositoryPath, filePath))
        {
            throw new FileNotFoundException(
                "The requested file is outside the repository.",
                relativePath);
        }

        return filePath;
    }

    public static bool IsPathInsideRepository(string repositoryPath, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string resolvedRepositoryPath = ResolveExistingPathComponents(repositoryPath);
        string resolvedFilePath = ResolveExistingPathComponents(filePath);
        string repositoryPathPrefix = Path.TrimEndingDirectorySeparator(resolvedRepositoryPath)
            + Path.DirectorySeparatorChar;

        return resolvedFilePath.StartsWith(
            repositoryPathPrefix,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveExistingPathComponents(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException("The path does not have a root.", nameof(path));
        string relativePath = Path.GetRelativePath(root, fullPath);
        string currentPath = root;

        foreach (string component in SplitPath(relativePath))
        {
            currentPath = Path.Combine(currentPath, component);
            FileSystemInfo? entry = GetExistingEntry(currentPath);
            if (entry is null || !entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            FileSystemInfo? target = entry.ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
            {
                currentPath = target.FullName;
            }
        }

        return Path.GetFullPath(currentPath);
    }

    private static IEnumerable<string> SplitPath(string relativePath)
    {
        return relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
    }

    private static FileSystemInfo? GetExistingEntry(string path)
    {
        if (Directory.Exists(path))
        {
            return new DirectoryInfo(path);
        }

        return File.Exists(path) ? new FileInfo(path) : null;
    }
}
