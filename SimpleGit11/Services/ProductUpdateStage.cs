namespace SimpleGit11.Services;

public enum ProductUpdateStage
{
    ValidateRelease,
    DownloadChecksum,
    DownloadChecksumViaApi,
    ValidateChecksum,
    PrepareDownload,
    DownloadInstaller,
    DownloadInstallerViaApi,
    VerifyInstaller,
    SaveInstaller,
    LaunchInstaller
}
