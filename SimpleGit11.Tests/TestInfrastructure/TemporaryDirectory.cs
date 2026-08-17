using System;
using System.IO;

namespace SimpleGit11.Tests.TestInfrastructure;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "SimpleGit11.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string CreateDirectory(string relativePath)
    {
        string directoryPath = GetPath(relativePath);
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    public string CreateFile(string relativePath, string content = "")
    {
        string filePath = GetPath(relativePath);
        string? directoryPath = System.IO.Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        File.WriteAllText(filePath, content);
        return filePath;
    }

    public string GetPath(string relativePath)
    {
        return System.IO.Path.Combine(Path, relativePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            ClearReadOnlyAttributes();
            Directory.Delete(Path, recursive: true);
        }
    }

    private void ClearReadOnlyAttributes()
    {
        foreach (string filePath in Directory.EnumerateFiles(
            Path,
            "*",
            SearchOption.AllDirectories))
        {
            FileAttributes attributes = File.GetAttributes(filePath);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
            }
        }
    }
}
