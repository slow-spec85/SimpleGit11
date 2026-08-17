using System.Diagnostics;
using System.IO;
using SimpleGit11.Services;

namespace SimpleGit11.Presentation.Services;

public sealed class FileExplorerService : IFileExplorerService
{
    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(path);
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = path,
            UseShellExecute = true
        };

        using Process? process = Process.Start(startInfo);
    }
}
