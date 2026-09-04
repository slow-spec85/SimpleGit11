using System;
using System.IO;

namespace SimpleGit11.Services.Plugins;

internal static class PluginPathPolicy
{
    public static void EnsureContainedPath(string pluginDirectory, string path)
    {
        string root = Path.GetFullPath(pluginDirectory);
        string relativePath = Path.GetRelativePath(root, Path.GetFullPath(path));
        if (Path.IsPathRooted(relativePath) || relativePath == ".."
            || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Plugin file paths must stay within the plugin directory.");
        }

        EnsureNotReparsePoint(root);
        string current = root;
        foreach (string segment in relativePath.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            EnsureNotReparsePoint(current);
        }
    }

    public static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Plugin directories and files must not be symbolic links or reparse points.");
        }
    }
}
