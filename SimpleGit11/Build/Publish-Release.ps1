#Requires -Version 5.1

[CmdletBinding()]
param(
    [switch]$DevelopmentBuild,
    [switch]$StopRunningApp
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "Publish-PathSafety.ps1")

[string]$projectDirectory = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
[string]$repositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $projectDirectory))
[string]$projectPath = Join-Path $projectDirectory "SimpleGit11.csproj"
[string]$projectAssetsPath = Join-Path $projectDirectory "obj\project.assets.json"
[string]$licenseCollectorPath = Join-Path $PSScriptRoot "Collect-ReleaseLicenses.ps1"
[string]$sourceComponentsPath = Join-Path $PSScriptRoot "SourceComponents.json"
[string]$artifactDirectory = Join-Path $repositoryRoot "artifacts"
[string]$stagingDirectory = Join-Path $artifactDirectory ".publish-staging-win-x64"
[string]$gitSafeDirectory = $repositoryRoot.Replace('\', '/')
[string]$semanticVersionCorePattern = '(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)'
[string]$prereleaseIdentifierPattern = '(?:(?:0|[1-9][0-9]*)|(?:[0-9]*[A-Za-z-][0-9A-Za-z-]*))'
[string]$semanticVersionPattern = '^{0}(?:-{1}(?:\.{1})*)?$' -f `
    $semanticVersionCorePattern,
    $prereleaseIdentifierPattern
[string]$releaseTagPattern = $semanticVersionPattern.Insert(1, "v")

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Project file was not found: $projectPath"
}

Assert-NoReparsePointUnderRoot -Path $projectPath -Root $repositoryRoot | Out-Null

if (-not (Test-Path -LiteralPath $licenseCollectorPath -PathType Leaf)) {
    throw "License collector was not found: $licenseCollectorPath"
}

if (-not (Test-Path -LiteralPath $sourceComponentsPath -PathType Leaf)) {
    throw "Source component manifest was not found: $sourceComponentsPath"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK was not found in PATH."
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "Git was not found in PATH."
}

function Invoke-RepositoryGit {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    [string[]]$output = @(& git -c "safe.directory=$gitSafeDirectory" -C $repositoryRoot @Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "Git exited with code ${LASTEXITCODE}: git $($Arguments -join ' ')"
    }

    return $output
}

[string]$expectedVersion = ""
[string]$developmentVersionOverride = ""

if ($DevelopmentBuild) {
    [string[]]$stableTags = @(Invoke-RepositoryGit -Arguments @(
        "tag",
        "--merged",
        "HEAD",
        "--sort=-v:refname",
        "--list",
        "v*"
    ) | Where-Object {
        $_ -match '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'
    })

    [int]$major = 1
    [int]$minor = 0
    [int]$patch = 0

    if ($stableTags.Count -gt 0 -and
        $stableTags[0] -match '^v(?<Major>[0-9]+)\.(?<Minor>[0-9]+)\.(?<Patch>[0-9]+)$') {
        $major = [int]$Matches.Major
        $minor = [int]$Matches.Minor
        $patch = [int]$Matches.Patch + 1
    }

    [string]$timestamp = [DateTime]::UtcNow.ToString("yyyyMMddHHmmss")
    $developmentVersionOverride = "$major.$minor.${patch}-dev.local.$timestamp"
    Write-Warning "DevelopmentBuild is enabled. Forcing prerelease version $developmentVersionOverride."
}
else {
    [string[]]$repositoryStatus = @(Invoke-RepositoryGit -Arguments @(
        "status",
        "--porcelain",
        "--untracked-files=normal"
    ))

    if ($repositoryStatus.Count -gt 0) {
        throw "The working tree is not clean. Commit all changes before preparing a release.`n$($repositoryStatus -join [Environment]::NewLine)"
    }

    [string[]]$headTags = @(Invoke-RepositoryGit -Arguments @(
        "tag",
        "--points-at",
        "HEAD",
        "--list",
        "v*"
    ))
    [string[]]$releaseTags = @($headTags | Where-Object {
        $_ -match $releaseTagPattern
    })

    if ($headTags.Count -ne 1 -or $releaseTags.Count -ne 1) {
        throw "HEAD must have exactly one release tag in vMAJOR.MINOR.PATCH[-PRERELEASE] format. Found: $($headTags -join ', ')"
    }

    [string]$releaseTag = $releaseTags[0]
    $expectedVersion = $releaseTag.Substring(1)
    Write-Host "Release tag: $releaseTag"
}

$runningProcesses = @(Get-Process -Name "SimpleGit11" -ErrorAction SilentlyContinue)
if ($runningProcesses.Count -gt 0) {
    if (-not $StopRunningApp) {
        throw "SimpleGit11 is running. Close it or run the script with -StopRunningApp."
    }

    Write-Host "Stopping running SimpleGit11 processes..."
    $runningProcesses | Stop-Process
    $runningProcesses | Wait-Process -Timeout 15
}

Assert-NoReparsePointUnderRoot -Path $artifactDirectory -Root $repositoryRoot | Out-Null
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
Assert-NoReparsePointUnderRoot -Path $artifactDirectory -Root $repositoryRoot | Out-Null
Remove-DirectoryUnderRoot -Path $stagingDirectory -Root $repositoryRoot
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
Assert-NoReparsePointUnderRoot -Path $stagingDirectory -Root $repositoryRoot | Out-Null

Write-Host "Publishing unpackaged self-contained win-x64 application..."
[string]$publishDirectoryArgument = "-p:PublishDir=$stagingDirectory\"
[string[]]$publishArguments = @(
    "publish",
    $projectPath,
    "-c",
    "Release",
    "-p:Platform=x64",
    "-p:PublishProfile=win-x64",
    $publishDirectoryArgument
)

if ($DevelopmentBuild) {
    $publishArguments += "-p:MinVerVersionOverride=$developmentVersionOverride"
}

& dotnet @publishArguments

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish exited with code $LASTEXITCODE."
}

Assert-NoReparsePointsInTree -Path $stagingDirectory -Root $repositoryRoot | Out-Null

Write-Host "Collecting release licenses and third-party notices..."
& $licenseCollectorPath `
    -RepositoryRoot $repositoryRoot `
    -ProjectAssetsPath $projectAssetsPath `
    -SourceComponentsPath $sourceComponentsPath `
    -PublishedDirectory $stagingDirectory
Assert-NoReparsePointsInTree -Path $stagingDirectory -Root $repositoryRoot | Out-Null

[string[]]$requiredFiles = @(
    "SimpleGit11.exe",
    "SimpleGit11.dll",
    "SimpleGit11.pri",
    "App.xbf",
    "MainWindow.xbf",
    "Controls\DiffViewer.xbf",
    "Dialogs\AboutDialog.xbf",
    "Dialogs\CommitDialog.xbf",
    "Pages\SettingsPage.xbf",
    "Assets\AppIcon.ico",
    "LICENSE",
    "THIRD-PARTY-NOTICES.txt",
    "Licenses\PACKAGES.txt"
)
[string[]]$missingFiles = @($requiredFiles | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $stagingDirectory $_) -PathType Leaf)
})

if ($missingFiles.Count -gt 0) {
    throw "Required publish files are missing: $($missingFiles -join ', ')"
}

[string]$executablePath = Join-Path $stagingDirectory "SimpleGit11.exe"
[string]$productVersion = (Get-Item -LiteralPath $executablePath).VersionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($productVersion)) {
    throw "ProductVersion could not be read from $executablePath."
}

[string]$artifactVersion = ($productVersion -split '\+', 2)[0]
if ($artifactVersion -notmatch $semanticVersionPattern) {
    throw "ProductVersion is not a valid SemVer version: $productVersion"
}

if (-not $DevelopmentBuild -and $artifactVersion -ne $expectedVersion) {
    throw "Published EXE version ($artifactVersion) does not match the release tag ($expectedVersion)."
}

if ($DevelopmentBuild -and $artifactVersion -ne $developmentVersionOverride) {
    throw "Published EXE version ($artifactVersion) does not match the development override ($developmentVersionOverride)."
}

[string]$artifactBaseName = "SimpleGit11-$artifactVersion-win-x64"
[string]$publishedApplicationDirectory = Join-Path $artifactDirectory $artifactBaseName
[string]$archivePath = Join-Path $artifactDirectory "$artifactBaseName.zip"
[string]$checksumPath = "$archivePath.sha256"

Remove-DirectoryUnderRoot -Path $publishedApplicationDirectory -Root $repositoryRoot
Remove-FileUnderRoot -Path $archivePath -Root $repositoryRoot
Remove-FileUnderRoot -Path $checksumPath -Root $repositoryRoot

Move-Item -LiteralPath $stagingDirectory -Destination $publishedApplicationDirectory
Assert-NoReparsePointsInTree `
    -Path $publishedApplicationDirectory `
    -Root $repositoryRoot | Out-Null

Write-Host "Creating ZIP archive..."
Assert-NoReparsePointUnderRoot -Path $archivePath -Root $repositoryRoot | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $publishedApplicationDirectory,
    $archivePath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf) -or
    (Get-Item -LiteralPath $archivePath).Length -eq 0) {
    throw "ZIP archive was not created: $archivePath"
}

$archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    [string[]]$archiveEntries = @($archive.Entries | ForEach-Object {
        $_.FullName.Replace('\', '/')
    })
    [string[]]$missingArchiveEntries = @($requiredFiles | Where-Object {
        $archiveEntries -notcontains $_.Replace('\', '/')
    })
}
finally {
    $archive.Dispose()
}

if ($missingArchiveEntries.Count -gt 0) {
    throw "Required ZIP entries are missing: $($missingArchiveEntries -join ', ')"
}

[string]$sha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
[string]$archiveFileName = Split-Path -Leaf $archivePath
Set-Content -LiteralPath $checksumPath -Value "$sha256  $archiveFileName" -Encoding Ascii

Write-Host ""
Write-Host "Publication artifacts are ready:" -ForegroundColor Green
Write-Host "  Version:     $artifactVersion"
Write-Host "  Application: $publishedApplicationDirectory"
Write-Host "  ZIP:         $archivePath"
Write-Host "  SHA-256:     $checksumPath"
Write-Host "  Hash:        $sha256"

[pscustomobject]@{
    Version = $artifactVersion
    ProductVersion = $productVersion
    PublishDirectory = $publishedApplicationDirectory
    Archive = $archivePath
    ChecksumFile = $checksumPath
    Sha256 = $sha256
}
