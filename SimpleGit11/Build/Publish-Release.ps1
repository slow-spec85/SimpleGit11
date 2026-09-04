#Requires -Version 5.1

[CmdletBinding()]
param(
    [switch]$DevelopmentBuild,
    [switch]$StopRunningApp,
    [switch]$Interactive,
    [string]$InstallerVersion,
    [switch]$AcceptWixEula
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot 'Installer-Payload.ps1')
$AcceptWixEula = Confirm-WixEula -Accepted:$AcceptWixEula -Interactive:$Interactive

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

[string]$installerReleaseVersion = if ($DevelopmentBuild) { $developmentVersionOverride } else { $expectedVersion }
[string]$msiVersion = Get-InstallerVersion -ReleaseVersion $installerReleaseVersion -Override $InstallerVersion
Write-Host "MSI product version: $msiVersion (application: $installerReleaseVersion)"
if ($installerReleaseVersion -match '-') {
    Write-Warning 'MSI ignores prerelease suffixes. Uninstall an existing package with the same or a higher numeric version before installing this build, or explicitly choose a higher -InstallerVersion.'
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

& dotnet @publishArguments | Out-Host

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

# The published directory is an internal MSI payload, not a separate distribution.
[string[]]$installerPaths = @(& (Join-Path $PSScriptRoot 'Build-Installer.ps1') `
    -CoreDirectory $stagingDirectory -ReleaseVersion $artifactVersion `
    -InstallerVersion $msiVersion -AcceptWixEula:$AcceptWixEula)
[string[]]$checksumPaths = @($installerPaths | ForEach-Object { "$_.sha256" })

Write-Host ""
Write-Host "Publication artifacts are ready:" -ForegroundColor Green
Write-Host "  Application version: $artifactVersion"
Write-Host "  MSI product version: $msiVersion"
Write-Host "  MSI:                 $($installerPaths -join ', ')"
Write-Host "  SHA-256:             $($checksumPaths -join ', ')"

[pscustomobject]@{
    Version = $artifactVersion
    ProductVersion = $productVersion
    InstallerVersion = $msiVersion
    Installers = $installerPaths
    ChecksumFiles = $checksumPaths
}
