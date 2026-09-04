using System;
using System.IO;

namespace SimpleGit11.Services.Execution.Local;

public sealed class LocalRepositoryPathService : IRepositoryPathService
{
    public RepositoryPathStyle Style => RepositoryPathStyle.Windows;

    public string Combine(string left, string right)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(left);
        ArgumentNullException.ThrowIfNull(right);
        return Path.Combine(left, right);
    }

    public string? GetParent(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Directory.GetParent(path)?.FullName;
    }

    public string GetFileName(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFileName(path);
    }

    public string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }
}
