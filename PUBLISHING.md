<p align="center">
  <a href="PUBLISHING.md">English</a> | <a href="PUBLISHING.ru.md">Русский</a>
</p>

# Building, installing, and updating SimpleGit11

## Ready-to-use distribution

SimpleGit11 is distributed as an **unpackaged, self-contained, win-x64**
application:

- MSIX and the Microsoft Store are not used;
- .NET and the Windows App SDK are included in the distribution;
- the application does not require certificate installation or administrator
  rights;
- the entire application directory is required, not just `SimpleGit11.exe`.

Each published version provides the following files on its Releases page:

```text
SimpleGit11-<version>-win-x64-en-US.msi
SimpleGit11-<version>-win-x64-en-US.msi.sha256
SimpleGit11-<version>-win-x64-ru-RU.msi
SimpleGit11-<version>-win-x64-ru-RU.msi.sha256
```

The automatically generated GitHub `Source code (zip)` and
`Source code (tar.gz)` archives contain source code, not the ready-to-use
application.

## System requirements

For the ready-to-use application:

- Windows 11 x64;
- Git for Windows.

Building from source additionally requires:

- .NET SDK 10 (minimum 10.0.100; later 10.0 feature bands and patch versions
  are allowed);
- a Windows SDK that supports Windows 10 version 19041 or later;
- NuGet access for dependency restore.

## Installation

### MSI with optional SSH

Choose `SimpleGit11-<version>-win-x64-en-US.msi` (or `ru-RU`).
Choose current-user installation (default: `%LOCALAPPDATA%\Programs\SimpleGit11`)
or all-users installation (default: `%ProgramFiles%\SimpleGit11`, administrator
approval required). The next page allows changing the folder. Core is
required; SSH and the desktop shortcut are optional, off by default. Re-run the
original MSI to modify, repair or uninstall. Close the app first. Upgrades preserve
feature selection, installation scope, custom folder and settings. Uninstall first
to change scope or move an existing installation. Do not install over a portable copy.

Current-user uninstall offers an unchecked option to delete all application data in
`%LOCALAPPDATA%\SimpleGit11`: settings, SSH profiles, history, logs and subfolders.
Back up needed data first. Repository/key paths outside that folder are not touched.
Junction directories are not traversed and may remain. Other users' profiles and
unknown third-party files in the binary directory remain. Feature removal, repairs
and upgrades never purge settings. All-users uninstall preserves every user's
personal data; it does not offer profile deletion, even with `PURGEUSERDATA=1`.

## Verifying SHA-256

Place the MSI and its matching `.sha256` file in the same directory,
then run the following in PowerShell:

```powershell
$installerPath = ".\SimpleGit11-1.0.0-win-x64-en-US.msi"
$checksum = "$installerPath.sha256"

$expected = (Get-Content $checksum).Split(
    " ",
    [System.StringSplitOptions]::RemoveEmptyEntries)[0]
$actual = (Get-FileHash $installerPath -Algorithm SHA256).Hash

if ($actual -ne $expected) {
    throw "SHA-256 checksum mismatch."
}

"SHA-256 checksum is valid."
```

Replace `1.0.0` with the downloaded version number.

## Updating

Automatic updates are not implemented yet.

To update the application manually:

1. Close every running SimpleGit11 instance.
2. Download the MSI for the new version.
3. Optionally verify its SHA-256 checksum.
4. Run the MSI and complete the upgrade.
5. Run `SimpleGit11.exe`.

Settings and the recent repository list are stored separately:

```text
%LOCALAPPDATA%\SimpleGit11\settings.json
```

MSI upgrades preserve user settings and feature selection.

## Building from source

Cloning the Git repository is preferable to downloading an automatically
generated source archive because MinVer uses Git tags to calculate the version.

Restore dependencies:

```powershell
dotnet restore .\SimpleGit11.slnx
```

Build the Debug configuration for `x64` only:

```powershell
dotnet build .\SimpleGit11.slnx `
  -c Debug `
  -p:Platform=x64
```

Build output:

```text
SimpleGit11\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\
```

Run the built application:

```powershell
& ".\SimpleGit11\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\SimpleGit11.exe"
```

## Preparing MSI packages and SHA-256 checksums

Publishing produces installers only. The script performs a self-contained
publish, collects licenses, checks required WinUI files, and builds English and
Russian MSI packages. WiX SDK 7 and its UI/Util extensions are build dependencies.
Read the [WiX terms](https://docs.firegiant.com/wix/osmf/) before passing
`-AcceptWixEula`.

```powershell
# Tagged stable release
.\Publish-Release.cmd.bat

# Tagged preview/rc: the numeric application version is used automatically
.\Publish-Release.cmd.bat

# Development build from any branch
.\Publish-Release-dev.cmd.bat
```

Release mode requires a clean working tree, exactly one
`vMAJOR.MINOR.PATCH[-PRERELEASE]` tag on `HEAD`, and an EXE version matching
that tag. The public release workflow accepts stable tags and the `preview.N`
and `rc.N` channels. Numeric prerelease identifiers cannot contain leading zeroes.

Development mode allows uncommitted changes and requires no tag. Its application
version is `<next-patch>-dev.local.<timestamp>`. MSI automatically uses the numeric
core: `1.0.0-dev.local.20260831...` becomes `1.0.0`. Windows Installed apps shows
that numeric version. Stable versions match exactly; all MSI versions must fit
`255.255.65535`.

Different packages with the same numeric version, and downgrades, remain blocked.
To replace a test build with another build of the same core version (including
stable), uninstall the previous package first; settings are preserved by default.
An optional `-InstallerVersion` override is still available for prereleases when
an increasing test version is needed. Stable versions cannot be overridden.

All BAT launchers work without arguments: they enable `-Interactive` and ask for
WiX consent in the console. Enter `y` or `yes` to accept for that build; Enter or
any other response cancels before building or stopping the application. Consent
is not saved. `-AcceptWixEula` remains available for unattended PowerShell calls
and skips the prompt when explicitly supplied to a BAT launcher.

Both BAT wrappers stop running SimpleGit11 instances. To require the application
to be closed manually, call PowerShell without `-StopRunningApp`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\SimpleGit11\Build\Publish-Release.ps1 `
  -DevelopmentBuild -AcceptWixEula
```

The distributable artifacts under `artifacts` are:

```text
SimpleGit11-<version>-win-x64-en-US.msi
SimpleGit11-<version>-win-x64-en-US.msi.sha256
SimpleGit11-<version>-win-x64-ru-RU.msi
SimpleGit11-<version>-win-x64-ru-RU.msi.sha256
```

The `.publish-staging-win-x64` and `.installer-staging-win-x64` directories
inside `artifacts` are internal build inputs, not separate distributions.
Older artifacts are not deleted automatically. The obsolete `-Installer` switch
has been removed: MSI generation is always enabled.

MSI payloads retain `LICENSE`, `THIRD-PARTY-NOTICES.txt` and `Licenses`.
Private SSH dependencies and their licenses belong to the optional feature.
Publishing fails if required files or licenses are missing. MSI tables, SSH
isolation and data-removal guards are validated during the build.

The scripts do not create commits, tags or GitHub Releases, push, install the MSI
or launch the application. Packages are currently unsigned. Before public
distribution, sign them with a production certificate and timestamp, verify the
signature and regenerate SHA-256.
See [the installer guide](SimpleGit11.Installer/README.md) for details.

## CI and release-tag validation

CI builds Release x64 and runs all solution tests, including the SSH plugin.
On a release tag, the tag format and the commit's membership in `main` are always
checked. The latest `ci.yml` run triggered by a push to `main` may be reused only
for the exact same commit SHA, after its successful Release x64 build and
application/SSH test steps have been confirmed through the GitHub API.

Older successes cannot hide a newer failed, cancelled or running CI. Missing
results, skipped steps, incomplete history or API errors cause the normal build
and tests to run again. The workflow summary links to the reused run attempt or
explains why a fresh check is needed. Only read access to Actions is required.

MSI generation and GitHub Release publication remain manual. A tag affects the
version calculated by MinVer, so reusing source-code test results does not replace
building and checking the actual tagged distribution locally.

## Versioning

The version is calculated by [MinVer](https://github.com/adamralph/minver) from
tags in the following format:

```text
vMAJOR.MINOR.PATCH[-PRERELEASE]
```

The project follows [Semantic Versioning 2.0.0](https://semver.org/):

- a patch release, such as `1.0.1`, contains fixes;
- a minor release, such as `1.1.0`, contains backward-compatible features;
- a major release, such as `2.0.0`, contains incompatible changes;
- a prerelease, such as `1.0.0-preview.1`, precedes the stable `1.0.0` release.

A commit tagged `v1.0.0-preview.1` is built as version `1.0.0-preview.1`, while
the `v1.0.0` tag produces stable version `1.0.0`. Untagged builds receive a
prerelease version that includes commit information.

## Signing and Windows warnings

An unpackaged application can run without a certificate. However, an unsigned
or newly published binary may trigger a Microsoft Defender SmartScreen
warning.

Download the distribution only from the official Releases page and verify the
published SHA-256 checksum.

## Additional resources

- [MinVer](https://github.com/adamralph/minver)
- [Self-contained deployment overview](https://learn.microsoft.com/windows/apps/package-and-deploy/self-contained-deploy/self-contained-deploy-overview)
- [Deploy unpackaged apps](https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app)
- [Semantic Versioning 2.0.0](https://semver.org/)
