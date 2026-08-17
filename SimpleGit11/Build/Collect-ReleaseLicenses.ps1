#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RepositoryRoot,

    [Parameter(Mandatory)]
    [string]$ProjectAssetsPath,

    [Parameter(Mandatory)]
    [string]$PublishedDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-PackageIdentity {
    param(
        [Parameter(Mandatory)]
        [string]$LibraryName
    )

    [int]$separatorIndex = $LibraryName.LastIndexOf('/')
    if ($separatorIndex -le 0 -or $separatorIndex -eq $LibraryName.Length - 1) {
        throw "Invalid NuGet library identity: $LibraryName"
    }

    return [pscustomobject]@{
        Id = $LibraryName.Substring(0, $separatorIndex)
        Version = $LibraryName.Substring($separatorIndex + 1)
    }
}

function Find-PackageDirectory {
    param(
        [Parameter(Mandatory)]
        [string]$LibraryName,

        [Parameter(Mandatory)]
        $Assets
    )

    $identity = Get-PackageIdentity -LibraryName $LibraryName
    $libraryProperty = $Assets.libraries.PSObject.Properties[$LibraryName]
    [string]$relativePackagePath = if ($null -ne $libraryProperty) {
        $libraryProperty.Value.path
    }
    else {
        "$($identity.Id.ToLowerInvariant())/$($identity.Version)"
    }

    foreach ($packageFolder in $Assets.packageFolders.PSObject.Properties.Name) {
        [string]$candidate = Join-Path $packageFolder $relativePackagePath
        if (Test-Path -LiteralPath $candidate -PathType Container) {
            return [System.IO.Path]::GetFullPath($candidate)
        }
    }

    throw "Restored NuGet package was not found: $LibraryName"
}

function Add-PackageById {
    param(
        [Parameter(Mandatory)]
        [string]$PackageId,

        [Parameter(Mandatory)]
        $Assets,

        [Parameter(Mandatory)]
        [System.Collections.Generic.HashSet[string]]$PackageNames
    )

    foreach ($libraryProperty in $Assets.libraries.PSObject.Properties) {
        $identity = Get-PackageIdentity -LibraryName $libraryProperty.Name
        if ($identity.Id.Equals($PackageId, [System.StringComparison]::OrdinalIgnoreCase)) {
            [void]$PackageNames.Add($libraryProperty.Name)
            return
        }
    }

    throw "Required NuGet package is missing from project.assets.json: $PackageId"
}

[string]$fullRepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
[string]$fullProjectAssetsPath = [System.IO.Path]::GetFullPath($ProjectAssetsPath)
[string]$fullPublishedDirectory = [System.IO.Path]::GetFullPath($PublishedDirectory)
[string]$applicationLicensePath = Join-Path $fullRepositoryRoot "LICENSE"
[string]$thirdPartyNoticesPath = Join-Path $fullRepositoryRoot "THIRD-PARTY-NOTICES.txt"

foreach ($requiredFile in @(
    $fullProjectAssetsPath,
    $applicationLicensePath,
    $thirdPartyNoticesPath
)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required license input was not found: $requiredFile"
    }
}

if (-not (Test-Path -LiteralPath $fullPublishedDirectory -PathType Container)) {
    throw "Published application directory was not found: $fullPublishedDirectory"
}

$assets = Get-Content -LiteralPath $fullProjectAssetsPath -Raw | ConvertFrom-Json
$runtimeTarget = $assets.targets.PSObject.Properties |
    Where-Object { $_.Name.EndsWith('/win-x64', [System.StringComparison]::OrdinalIgnoreCase) } |
    Select-Object -First 1
if ($null -eq $runtimeTarget) {
    throw "The win-x64 restore target was not found in project.assets.json."
}

$publishedFileNames = New-Object 'System.Collections.Generic.HashSet[string]' `
    ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($publishedFile in Get-ChildItem `
    -LiteralPath $fullPublishedDirectory `
    -File `
    -Recurse `
    -Force) {
    [void]$publishedFileNames.Add($publishedFile.Name)
}

$includedPackages = New-Object 'System.Collections.Generic.HashSet[string]' `
    ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($libraryProperty in $runtimeTarget.Value.PSObject.Properties) {
    $libraryMetadata = $assets.libraries.PSObject.Properties[$libraryProperty.Name]
    if ($null -eq $libraryMetadata -or $libraryMetadata.Value.type -ne "package") {
        continue
    }

    [bool]$isPublished = $false
    foreach ($assetGroupName in @("runtime", "native", "runtimeTargets", "resource")) {
        $assetGroupProperty = $libraryProperty.Value.PSObject.Properties[$assetGroupName]
        if ($null -eq $assetGroupProperty) {
            continue
        }

        foreach ($assetProperty in $assetGroupProperty.Value.PSObject.Properties) {
            [string]$assetFileName = [System.IO.Path]::GetFileName($assetProperty.Name)
            if ($publishedFileNames.Contains($assetFileName)) {
                $isPublished = $true
                break
            }
        }

        if ($isPublished) {
            break
        }
    }

    if ($isPublished) {
        [void]$includedPackages.Add($libraryProperty.Name)
    }
}

# Microsoft.WindowsAppSDK is a meta-package whose license governs runtime
# components copied by its self-contained deployment targets.
Add-PackageById `
    -PackageId "Microsoft.WindowsAppSDK" `
    -Assets $assets `
    -PackageNames $includedPackages

$framework = $assets.project.frameworks.PSObject.Properties |
    Select-Object -First 1
foreach ($runtimePackageId in @(
    "Microsoft.NETCore.App.Runtime.win-x64",
    "Microsoft.WindowsDesktop.App.Runtime.win-x64"
)) {
    $downloadDependency = $framework.Value.downloadDependencies |
        Where-Object { $_.name -eq $runtimePackageId } |
        Select-Object -First 1
    if ($null -eq $downloadDependency) {
        throw "Required self-contained runtime pack is missing: $runtimePackageId"
    }

    [string]$runtimeVersion = $downloadDependency.version.Trim('[', ']').Split(',')[0].Trim()
    [void]$includedPackages.Add("$runtimePackageId/$runtimeVersion")
}

[string]$licensesDirectory = Join-Path $fullPublishedDirectory "Licenses"
if (Test-Path -LiteralPath $licensesDirectory) {
    throw "The license output directory already exists: $licensesDirectory"
}

New-Item -ItemType Directory -Path $licensesDirectory | Out-Null
Copy-Item -LiteralPath $applicationLicensePath -Destination (Join-Path $fullPublishedDirectory "LICENSE")
Copy-Item `
    -LiteralPath $thirdPartyNoticesPath `
    -Destination (Join-Path $fullPublishedDirectory "THIRD-PARTY-NOTICES.txt")

$packageIndex = New-Object 'System.Collections.Generic.List[string]'
$packageIndex.Add("Third-party packages included in this SimpleGit11 release")
$packageIndex.Add("=========================================================")
$packageIndex.Add("")
$packageIndex.Add("This file is generated from project.assets.json and the actual publish output.")
$packageIndex.Add("Original vendor license and notice files are stored in adjacent directories.")
$packageIndex.Add("")

foreach ($libraryName in @($includedPackages | Sort-Object)) {
    $identity = Get-PackageIdentity -LibraryName $libraryName
    [string]$packageDirectory = Find-PackageDirectory -LibraryName $libraryName -Assets $assets
    $nuspec = Get-ChildItem -LiteralPath $packageDirectory -File -Filter "*.nuspec" |
        Select-Object -First 1
    if ($null -eq $nuspec) {
        throw "NuGet metadata was not found for $libraryName."
    }

    [xml]$nuspecDocument = Get-Content -LiteralPath $nuspec.FullName -Raw
    $metadata = $nuspecDocument.package.metadata
    $licenseProperty = $metadata.PSObject.Properties["license"]
    if ($null -ne $licenseProperty) {
        $licenseNode = $licenseProperty.Value
        [string]$licenseType = $licenseNode.type
        [string]$licenseValue = $licenseNode.'#text'
    }
    elseif ($identity.Id -in @("Collections.Pooled", "Microsoft.Graphics.Win2D")) {
        # These package versions predate current NuGet license metadata. Their
        # upstream projects use MIT; package copyrights remain in PACKAGES.txt.
        [string]$licenseType = "expression"
        [string]$licenseValue = "MIT"
    }
    else {
        throw "NuGet package does not declare a license: $libraryName"
    }
    if ($licenseType -eq "expression" -and $licenseValue -ne "MIT") {
        throw "Unsupported license expression '$licenseValue' for $libraryName. Update the release license bundle before publishing."
    }

    [string]$packageLicenseDirectoryName = "$($identity.Id)-$($identity.Version)" -replace '[^0-9A-Za-z._-]', '_'
    [string]$packageLicenseDirectory = Join-Path $licensesDirectory $packageLicenseDirectoryName
    [string[]]$vendorFiles = @(
        Get-ChildItem -LiteralPath $packageDirectory -File |
            Where-Object {
                $_.Name -match '^(?i:license|notice|third[-_ ]?party)'
            } |
            Sort-Object Name |
            Select-Object -ExpandProperty FullName
    )

    if ($licenseType -eq "file") {
        [string]$declaredLicensePath = Join-Path $packageDirectory $licenseValue
        if (-not (Test-Path -LiteralPath $declaredLicensePath -PathType Leaf)) {
            throw "Declared license file was not found for ${libraryName}: $licenseValue"
        }

        if ($vendorFiles -notcontains $declaredLicensePath) {
            $vendorFiles += $declaredLicensePath
        }
    }

    if ($vendorFiles.Count -gt 0) {
        New-Item -ItemType Directory -Path $packageLicenseDirectory | Out-Null
        foreach ($vendorFile in $vendorFiles) {
            Copy-Item -LiteralPath $vendorFile -Destination $packageLicenseDirectory
        }
    }

    [string]$copyright = [string]$metadata.copyright
    [string]$projectUrl = [string]$metadata.projectUrl
    $packageIndex.Add("Package: $($identity.Id)")
    $packageIndex.Add("Version: $($identity.Version)")
    $packageIndex.Add("License: $licenseValue")
    if (-not [string]::IsNullOrWhiteSpace($copyright)) {
        $packageIndex.Add("Copyright: $copyright")
    }
    if (-not [string]::IsNullOrWhiteSpace($projectUrl)) {
        $packageIndex.Add("Project: $projectUrl")
    }
    if ($vendorFiles.Count -gt 0) {
        $packageIndex.Add("Bundled notices: Licenses/$packageLicenseDirectoryName")
    }
    $packageIndex.Add("")
}

[string]$packageIndexPath = Join-Path $licensesDirectory "PACKAGES.txt"
Set-Content -LiteralPath $packageIndexPath -Value $packageIndex -Encoding UTF8

Write-Host "Collected licenses for $($includedPackages.Count) redistributed packages."
