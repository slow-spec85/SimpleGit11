using System;
using System.Diagnostics;
using System.IO;

namespace SimpleGit11.Services;

internal static class ExceptionLogWriter
{
    private const string ApplicationDirectoryName = "SimpleGit11";
    private const string LogsDirectoryName = "Logs";

    public static void Write(string fileName, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(exception);

        Debug.WriteLine(exception);

        try
        {
            string logFilePath = GetLogFilePath(fileName);
            string logDirectoryPath = Path.GetDirectoryName(logFilePath)
                ?? throw new InvalidOperationException("The exception log directory could not be determined.");

            Directory.CreateDirectory(logDirectoryPath);
            File.WriteAllText(logFilePath, exception.ToString());
        }
        catch (Exception logException)
        {
            Debug.WriteLine(logException);
        }
    }

    internal static string GetLogFilePath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            throw new ArgumentException("The exception log file name must not contain a path.", nameof(fileName));
        }

        string localApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationDataPath, ApplicationDirectoryName, LogsDirectoryName, fileName);
    }
}
