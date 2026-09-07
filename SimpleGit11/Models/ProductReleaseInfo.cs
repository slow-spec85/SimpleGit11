using System;

namespace SimpleGit11.Models;

public sealed record ProductReleaseInfo(
    string Version,
    Uri Uri,
    bool IsPrerelease,
    ProductReleaseAsset? Installer = null);

public sealed record ProductReleaseAsset(
    string FileName,
    Uri DownloadUri,
    Uri ChecksumUri,
    long Size);
