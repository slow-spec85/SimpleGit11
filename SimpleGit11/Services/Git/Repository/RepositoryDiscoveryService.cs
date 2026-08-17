using System;
using System.IO;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

public sealed class RepositoryDiscoveryService : IGitRepositoryDiscoveryService
{
    public RepositoryInfo? TryOpenRepository(string path)
    {
        string? root = FindRepositoryRoot(path);
        if (root is null)
        {
            return null;
        }

        string? gitDirectory = ResolveGitPath(root);
        if (gitDirectory is null)
        {
            return null;
        }

        string commonGitDirectory = ResolveCommonGitDirectory(gitDirectory);
        bool isMainWorktree = PathsEqual(gitDirectory, commonGitDirectory);
        string mainWorktreePath = Directory.GetParent(commonGitDirectory)?.FullName ?? root;

        return new RepositoryInfo(
            root,
            new DirectoryInfo(root).Name,
            ReadCurrentBranch(gitDirectory),
            commonGitDirectory,
            mainWorktreePath,
            isMainWorktree);
    }

    private static string? FindRepositoryRoot(string path)
    {
        DirectoryInfo? directory = new(path);
        while (directory is not null)
        {
            string gitDirectory = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitDirectory) || File.Exists(gitDirectory))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string ReadCurrentBranch(string gitDirectory)
    {
        string headPath = Path.Combine(gitDirectory, "HEAD");
        if (!File.Exists(headPath))
        {
            return "Unknown";
        }

        string head = File.ReadAllText(headPath).Trim();
        const string branchPrefix = "ref: refs/heads/";
        if (head.StartsWith(branchPrefix, StringComparison.Ordinal))
        {
            return head[branchPrefix.Length..];
        }

        return head.Length > 7 ? $"Detached at {head[..7]}" : "Detached HEAD";
    }

    private static string? ResolveGitPath(string repositoryRoot)
    {
        string dotGitPath = Path.Combine(repositoryRoot, ".git");
        if (Directory.Exists(dotGitPath))
        {
            return Path.GetFullPath(dotGitPath);
        }

        if (!File.Exists(dotGitPath))
        {
            return null;
        }

        string content = File.ReadAllText(dotGitPath).Trim();
        const string gitDirPrefix = "gitdir:";
        if (!content.StartsWith(gitDirPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string gitDir = content[gitDirPrefix.Length..].Trim();
        return Path.GetFullPath(Path.IsPathRooted(gitDir)
            ? gitDir
            : Path.Combine(repositoryRoot, gitDir));
    }

    private static string ResolveCommonGitDirectory(string gitDirectory)
    {
        string commonDirectoryFile = Path.Combine(gitDirectory, "commondir");
        if (!File.Exists(commonDirectoryFile))
        {
            return Path.GetFullPath(gitDirectory);
        }

        string commonDirectory = File.ReadAllText(commonDirectoryFile).Trim();
        return Path.GetFullPath(Path.IsPathRooted(commonDirectory)
            ? commonDirectory
            : Path.Combine(gitDirectory, commonDirectory));
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }
}
