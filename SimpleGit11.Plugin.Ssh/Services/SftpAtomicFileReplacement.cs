using System;

namespace SimpleGit11.Plugin.Ssh.Services;

internal static class SftpAtomicFileReplacement
{
    public static void ReplaceWithoutPosixExtension(
        string temporaryPath,
        string destinationPath,
        Func<string, bool> exists,
        Action<string, string> rename,
        Action<string> delete)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(rename);
        ArgumentNullException.ThrowIfNull(delete);

        if (!exists(destinationPath))
        {
            rename(temporaryPath, destinationPath);
            return;
        }

        string backupPath = $"{destinationPath}.{Guid.NewGuid():N}.bak";
        rename(destinationPath, backupPath);
        try
        {
            rename(temporaryPath, destinationPath);
        }
        catch
        {
            if (!exists(destinationPath) && exists(backupPath))
            {
                rename(backupPath, destinationPath);
            }

            throw;
        }

        if (exists(backupPath))
        {
            delete(backupPath);
        }
    }
}
