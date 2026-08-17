namespace SimpleGit11.Models;

public enum GitArchiveFormat
{
    Zip,
    TarGZip,
    Tar
}

public sealed record GitArchiveDialogResult(
    string StartPoint,
    string ResolvedCommitHash,
    GitArchiveFormat Format,
    bool IncludeRootDirectory);

public sealed record GitArchiveRequest(
    string Revision,
    string OutputPath,
    GitArchiveFormat Format,
    string RootDirectoryName);
