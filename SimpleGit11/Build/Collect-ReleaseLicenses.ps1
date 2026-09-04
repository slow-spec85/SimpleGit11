#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RepositoryRoot,

    [Parameter(Mandatory)]
    [string]$ProjectAssetsPath,

    [Parameter(Mandatory)]
    [string]$SourceComponentsPath,

    [Parameter(Mandatory)]
    [string]$PublishedDirectory,

    [switch]$Plugin
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

function Get-RequiredComponentValue {
    param(
        [Parameter(Mandatory)]
        $Component,

        [Parameter(Mandatory)]
        [string]$PropertyName
    )

    $property = $Component.PSObject.Properties[$PropertyName]
    if ($null -eq $property -or
        [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        throw "Source component property is required: $PropertyName"
    }

    return [string]$property.Value
}

function Resolve-PathUnderRoot {
    param(
        [Parameter(Mandatory)]
        [string]$Root,

        [Parameter(Mandatory)]
        [string]$RelativePath,

        [Parameter(Mandatory)]
        [string]$Description
    )

    if ([System.IO.Path]::IsPathRooted($RelativePath)) {
        throw "$Description must be relative to the repository root: $RelativePath"
    }

    [string]$fullRoot = [System.IO.Path]::GetFullPath($Root)
    [string]$fullPath = [System.IO.Path]::GetFullPath((Join-Path $fullRoot $RelativePath))
    [char[]]$directorySeparators = @(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    [string]$rootPrefix = $fullRoot.TrimEnd($directorySeparators) +
        [System.IO.Path]::DirectorySeparatorChar

    if (-not $fullPath.StartsWith(
        $rootPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description escapes the repository root: $RelativePath"
    }

    return $fullPath
}

function Get-SourceRevision {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryPath
    )

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw "Git is required to determine source component revisions."
    }

    [string]$gitSafeDirectory = $RepositoryPath.Replace('\', '/')
    [string[]]$revisionOutput = @(& git `
        -c "safe.directory=$gitSafeDirectory" `
        -C $RepositoryPath `
        rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or
        $revisionOutput.Count -ne 1 -or
        $revisionOutput[0] -notmatch '^[0-9a-fA-F]{40,64}$') {
        throw "Could not determine the Git revision of source component: $RepositoryPath"
    }

    return $revisionOutput[0].ToLowerInvariant()
}

[string]$fullRepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
[string]$fullProjectAssetsPath = [System.IO.Path]::GetFullPath($ProjectAssetsPath)
[string]$fullSourceComponentsPath = [System.IO.Path]::GetFullPath($SourceComponentsPath)
[string]$fullPublishedDirectory = [System.IO.Path]::GetFullPath($PublishedDirectory)
[string]$applicationLicensePath = Join-Path $fullRepositoryRoot "LICENSE"
[string]$thirdPartyNoticesPath = Join-Path $fullRepositoryRoot "THIRD-PARTY-NOTICES.txt"

foreach ($requiredFile in @(
    $fullProjectAssetsPath,
    $fullSourceComponentsPath,
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
$sourceComponentsDocument = Get-Content `
    -LiteralPath $fullSourceComponentsPath `
    -Raw | ConvertFrom-Json
$sourceComponentDefinitions = New-Object 'System.Collections.Generic.List[object]'
# A plugin ships only its private dependencies, not the host's source components.
if (-not $Plugin) {
    if ($sourceComponentsDocument -is [System.Array]) {
        foreach ($componentDefinition in $sourceComponentsDocument) {
            $sourceComponentDefinitions.Add($componentDefinition)
        }
    }
    else {
        $sourceComponentDefinitions.Add($sourceComponentsDocument)
    }
}
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

[string]$mainProjectPath = [string]$assets.project.restore.projectPath
if ([string]::IsNullOrWhiteSpace($mainProjectPath)) {
    throw "The root project path is missing from project.assets.json."
}

[string]$mainProjectDirectory = Split-Path -Parent (
    [System.IO.Path]::GetFullPath($mainProjectPath))
$sourceComponents = New-Object 'System.Collections.Generic.List[object]'
foreach ($componentDefinition in $sourceComponentDefinitions) {
    [string]$componentName = Get-RequiredComponentValue `
        -Component $componentDefinition `
        -PropertyName "name"
    [string]$componentProjectRelativePath = Get-RequiredComponentValue `
        -Component $componentDefinition `
        -PropertyName "projectPath"
    [string]$componentRepositoryRelativePath = Get-RequiredComponentValue `
        -Component $componentDefinition `
        -PropertyName "repositoryPath"
    [string]$componentPublishedFile = Get-RequiredComponentValue `
        -Component $componentDefinition `
        -PropertyName "publishedFile"
    [string]$componentLicenseRelativePath = Get-RequiredComponentValue `
        -Component $componentDefinition `
        -PropertyName "licenseFile"
    [string]$componentLicense = Get-RequiredComponentValue `
        -Component $componentDefinition `
        -PropertyName "license"
    [string]$componentCopyright = Get-RequiredComponentValue `
        -Component $componentDefinition `
        -PropertyName "copyright"
    [string]$componentProjectUrl = Get-RequiredComponentValue `
        -Component $componentDefinition `
        -PropertyName "projectUrl"

    if ($componentPublishedFile -ne [System.IO.Path]::GetFileName($componentPublishedFile)) {
        throw "Source component publishedFile must contain only a file name: $componentPublishedFile"
    }
    if ($componentLicense -ne "MIT") {
        throw "Unsupported source component license '$componentLicense' for $componentName."
    }

    [string]$componentProjectPath = Resolve-PathUnderRoot `
        -Root $fullRepositoryRoot `
        -RelativePath $componentProjectRelativePath `
        -Description "Source component project path"
    [string]$componentRepositoryPath = Resolve-PathUnderRoot `
        -Root $fullRepositoryRoot `
        -RelativePath $componentRepositoryRelativePath `
        -Description "Source component repository path"
    [string]$componentLicensePath = Resolve-PathUnderRoot `
        -Root $fullRepositoryRoot `
        -RelativePath $componentLicenseRelativePath `
        -Description "Source component license path"

    foreach ($requiredComponentFile in @(
        $componentProjectPath,
        $componentLicensePath
    )) {
        if (-not (Test-Path -LiteralPath $requiredComponentFile -PathType Leaf)) {
            throw "Required source component file was not found: $requiredComponentFile"
        }
    }
    if (-not (Test-Path -LiteralPath $componentRepositoryPath -PathType Container)) {
        throw "Source component repository was not found: $componentRepositoryPath"
    }
    if (-not $publishedFileNames.Contains($componentPublishedFile)) {
        throw "Published source component file was not found: $componentPublishedFile"
    }

    [string]$componentLicenseText = Get-Content `
        -LiteralPath $componentLicensePath `
        -Raw
    if ($componentLicenseText.IndexOf(
        $componentCopyright,
        [System.StringComparison]::Ordinal) -lt 0) {
        throw "Source component license does not contain its declared copyright: $componentName"
    }

    $matchingProjectLibrary = $null
    foreach ($libraryProperty in $assets.libraries.PSObject.Properties) {
        if ($libraryProperty.Value.type -ne "project") {
            continue
        }

        [string]$msbuildProject = [string]$libraryProperty.Value.msbuildProject
        if ([string]::IsNullOrWhiteSpace($msbuildProject)) {
            continue
        }

        [string]$referencedProjectPath = [System.IO.Path]::GetFullPath((
            Join-Path $mainProjectDirectory $msbuildProject))
        if ($referencedProjectPath.Equals(
            $componentProjectPath,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            $matchingProjectLibrary = $libraryProperty
            break
        }
    }

    if ($null -eq $matchingProjectLibrary) {
        throw "Source component is not a restored ProjectReference: $componentName"
    }

    $projectIdentity = Get-PackageIdentity -LibraryName $matchingProjectLibrary.Name
    if (-not $projectIdentity.Id.Equals(
        $componentName,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Source component name '$componentName' does not match restored project '$($projectIdentity.Id)'."
    }

    [string]$componentRevision = Get-SourceRevision `
        -RepositoryPath $componentRepositoryPath
    $sourceComponents.Add([pscustomobject]@{
        Name = $componentName
        Revision = $componentRevision
        License = $componentLicense
        Copyright = $componentCopyright
        ProjectUrl = $componentProjectUrl
        LicensePath = $componentLicensePath
    })
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
if (-not $Plugin) {
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
$packageIndex.Add("Third-party components included in this SimpleGit11 release")
$packageIndex.Add("===========================================================")
$packageIndex.Add("")
$packageIndex.Add("This file is generated from project.assets.json, SourceComponents.json, and the actual publish output.")
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

    [string]$copyright = $metadata.CreateNavigator().Evaluate("string(*[local-name()='copyright'])")
    [string]$projectUrl = $metadata.CreateNavigator().Evaluate("string(*[local-name()='projectUrl'])")
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

foreach ($component in @($sourceComponents | Sort-Object Name)) {
    [string]$shortRevision = $component.Revision.Substring(0, 12)
    [string]$componentLicenseDirectoryName = "$($component.Name)-$shortRevision" `
        -replace '[^0-9A-Za-z._-]', '_'
    [string]$componentLicenseDirectory = Join-Path `
        $licensesDirectory `
        $componentLicenseDirectoryName

    New-Item -ItemType Directory -Path $componentLicenseDirectory | Out-Null
    Copy-Item `
        -LiteralPath $component.LicensePath `
        -Destination $componentLicenseDirectory

    $packageIndex.Add("Component: $($component.Name)")
    $packageIndex.Add("Revision: $($component.Revision)")
    $packageIndex.Add("License: $($component.License)")
    $packageIndex.Add("Copyright: $($component.Copyright)")
    $packageIndex.Add("Project: $($component.ProjectUrl)")
    $packageIndex.Add("Bundled notices: Licenses/$componentLicenseDirectoryName")
    $packageIndex.Add("")
}

[string]$packageIndexPath = Join-Path $licensesDirectory "PACKAGES.txt"
Set-Content -LiteralPath $packageIndexPath -Value $packageIndex -Encoding UTF8

Write-Host "Collected licenses for $($includedPackages.Count) redistributed packages and $($sourceComponents.Count) source components."
