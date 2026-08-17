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
SimpleGit11-<version>-win-x64.zip
SimpleGit11-<version>-win-x64.zip.sha256
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

1. Open the required published version on the Releases page.
2. Download `SimpleGit11-<version>-win-x64.zip`.
3. Optionally verify its SHA-256 checksum as described below.
4. Create a permanent directory, for example:

```text
%LOCALAPPDATA%\Programs\SimpleGit11
```

5. Extract the entire ZIP archive into this directory.
6. Run `SimpleGit11.exe`.
7. Optionally create a shortcut manually.

Do not run the application directly from the ZIP archive or copy only the EXE
file.

## Verifying SHA-256

Place the ZIP archive and its matching `.sha256` file in the same directory,
then run the following in PowerShell:

```powershell
$archive = ".\SimpleGit11-1.0.0-win-x64.zip"
$checksum = "$archive.sha256"

$expected = (Get-Content $checksum).Split(
    " ",
    [System.StringSplitOptions]::RemoveEmptyEntries)[0]
$actual = (Get-FileHash $archive -Algorithm SHA256).Hash

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
2. Download the ZIP archive for the new version.
3. Optionally verify its SHA-256 checksum.
4. Completely replace the files in the application directory with the
   contents of the new ZIP archive.
5. Run `SimpleGit11.exe`.

Settings and the recent repository list are stored separately:

```text
%LOCALAPPDATA%\SimpleGit11\settings.json
```

Replacing the application directory does not remove user settings.

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

## Preparing a ZIP archive and SHA-256 checksum

The repository contains scripts that perform a self-contained publish, verify
required WinUI files, and create ready-to-distribute artifacts in the
`artifacts` directory.

For a stable or prerelease build, run:

```powershell
.\Publish-Release.cmd.bat
```

Release mode requires:

- a clean working tree;
- exactly one `vMAJOR.MINOR.PATCH[-PRERELEASE]` tag pointing to `HEAD`;
- the built EXE version to match the tag version.

Examples of valid release tags:

```text
v1.0.0-preview.1
v1.0.0-rc.1
v1.0.0
```

The public release workflow accepts stable tags and prerelease tags using the
`preview.N` and `rc.N` channels.

Numeric prerelease identifiers must not contain leading zeroes: `preview.1` is
valid, while `preview.01` is not.

For a test build from any branch, run:

```powershell
.\Publish-Release-dev.cmd.bat
```

Development mode does not require a clean working tree or a tag on `HEAD`. The
artifact receives a unique prerelease version in the form
`<next-patch>-dev.local.<timestamp>`.

Both BAT files close a running SimpleGit11 instance before publishing and
create:

```text
artifacts\SimpleGit11-<version>-win-x64\
artifacts\SimpleGit11-<version>-win-x64.zip
artifacts\SimpleGit11-<version>-win-x64.zip.sha256
```

The application directory and ZIP archive also contain `LICENSE`,
`THIRD-PARTY-NOTICES.txt`, and a `Licenses` directory with exact package
versions and original license files for the components that are actually
redistributed. Publishing fails if the license of a new runtime package cannot
be determined automatically.

To run the PowerShell script directly without the BAT wrapper:

```powershell
# Stable or prerelease build
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\SimpleGit11\Build\Publish-Release.ps1 `
  -StopRunningApp

# Development build
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\SimpleGit11\Build\Publish-Release.ps1 `
  -DevelopmentBuild `
  -StopRunningApp
```

The scripts only prepare local artifacts. They do not create commits or tags,
run `push`, or create a GitHub Release.

## Local self-contained publish

If a ZIP archive and checksum are not required, publish directly:

```powershell
dotnet publish .\SimpleGit11\SimpleGit11.csproj `
  -c Release `
  -p:Platform=x64 `
  -p:PublishProfile=win-x64
```

Published directory:

```text
SimpleGit11\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\
```

The publish profile adds the required WinUI resources:

- XBF files;
- `SimpleGit11.pri`;
- the `Assets` directory;
- the .NET and Windows App SDK runtimes.

Distribute the entire contents of the `publish` directory.

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
