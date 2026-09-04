using System;
using System.Collections.Generic;
using System.IO;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Plugin.Ssh.Services;

public sealed class RemoteRepositoryPathService : IRepositoryPathService
{
    public RemoteRepositoryPathService(RepositoryPathStyle style)
    {
        Style = style;
    }

    public RepositoryPathStyle Style { get; }

    public string Combine(string left, string right)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(left);
        ArgumentNullException.ThrowIfNull(right);
        return Style == RepositoryPathStyle.Windows
            ? Path.Combine(left, right)
            : $"{left.TrimEnd('/')}/{right.TrimStart('/')}";
    }

    public string? GetParent(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Style == RepositoryPathStyle.Windows)
        {
            return Path.GetDirectoryName(path);
        }

        string normalized = Normalize(path).TrimEnd('/');
        int separator = normalized.LastIndexOf('/');
        return separator <= 0 ? (normalized.StartsWith('/') ? "/" : null) : normalized[..separator];
    }

    public string GetFileName(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Style == RepositoryPathStyle.Windows
            ? Path.GetFileName(path)
            : path.TrimEnd('/').Split('/')[^1];
    }

    public string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Style == RepositoryPathStyle.Windows)
        {
            return Path.GetFullPath(path);
        }

        bool rooted = path.StartsWith('/');
        Stack<string> segments = new();
        foreach (string segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0 && segments.Peek() != "..")
                {
                    segments.Pop();
                }
                else if (!rooted)
                {
                    segments.Push(segment);
                }

                continue;
            }

            segments.Push(segment);
        }

        string[] normalizedSegments = segments.ToArray();
        Array.Reverse(normalizedSegments);
        string normalized = string.Join('/', normalizedSegments);
        return rooted ? $"/{normalized}" : normalized;
    }
}
