#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CoreDirectory,
    [Parameter(Mandatory)][string]$ReleaseVersion,
    [string]$InstallerVersion,
    [switch]$AcceptWixEula
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Installer-Payload.ps1')

if (-not $AcceptWixEula) {
    throw 'Read https://docs.firegiant.com/wix/osmf/ and explicitly pass -AcceptWixEula.'
}
[string]$version = Get-InstallerVersion -ReleaseVersion $ReleaseVersion -Override $InstallerVersion
$CoreDirectory = [IO.Path]::GetFullPath($CoreDirectory)
[string]$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
[string]$artifactDirectory = Join-Path $repositoryRoot 'artifacts'
[string]$staging = Join-Path $artifactDirectory '.installer-staging-win-x64'
Assert-NoReparsePointsInTree -Path $CoreDirectory -Root $repositoryRoot | Out-Null
[string]$coreVersion = ((Get-Item -LiteralPath (Join-Path $CoreDirectory 'SimpleGit11.exe')).VersionInfo.ProductVersion -split '\+', 2)[0]
if ($coreVersion -ne $ReleaseVersion) { throw "Core payload version $coreVersion does not match $ReleaseVersion." }
Remove-DirectoryUnderRoot -Path $staging -Root $repositoryRoot
New-Item -ItemType Directory -Path $staging | Out-Null
[string]$sshDirectory = Join-Path $staging 'Ssh'
New-Item -ItemType Directory -Path $sshDirectory | Out-Null

Write-Host 'Building the optional SSH component...'
[string]$pluginProject = Join-Path $repositoryRoot 'SimpleGit11.Plugin.Ssh\SimpleGit11.Plugin.Ssh.csproj'
& dotnet build $pluginProject -c Release -p:Platform=x64 "-p:MinVerVersionOverride=$ReleaseVersion" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "SSH build failed: $LASTEXITCODE" }
[string]$pluginOutput = Join-Path $repositoryRoot 'SimpleGit11.Plugin.Ssh\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\Plugin'
Assert-NoReparsePointsInTree -Path $pluginOutput -Root $repositoryRoot | Out-Null
[string]$pluginManifestPath = Join-Path $pluginOutput 'plugin.json'
$pluginManifest = Get-Content -LiteralPath $pluginManifestPath -Raw | ConvertFrom-Json
if ($pluginManifest.version -ne $ReleaseVersion) {
    throw "SSH plugin version $($pluginManifest.version) does not match application version $ReleaseVersion."
}
foreach ($file in @('plugin.json', 'SimpleGit11.Plugin.Ssh.dll', 'SimpleGit11.Plugin.Ssh.deps.json', 'Renci.SshNet.dll', 'BouncyCastle.Cryptography.dll', 'Microsoft.Extensions.Logging.Abstractions.dll')) {
    Copy-Item -LiteralPath (Join-Path $pluginOutput $file) -Destination $sshDirectory
}
& (Join-Path $PSScriptRoot 'Collect-ReleaseLicenses.ps1') `
    -RepositoryRoot $repositoryRoot `
    -ProjectAssetsPath (Join-Path $repositoryRoot 'SimpleGit11.Plugin.Ssh\obj\project.assets.json') `
    -SourceComponentsPath (Join-Path $PSScriptRoot 'SourceComponents.json') `
    -PublishedDirectory $sshDirectory -Plugin
[string]$payload = Join-Path $staging 'Payload.wxs'
Write-InstallerPayload -CoreDirectory $CoreDirectory -SshDirectory $sshDirectory -OutputPath $payload -RepositoryRoot $repositoryRoot

[string]$installerProject = Join-Path $repositoryRoot 'SimpleGit11.Installer\SimpleGit11.Installer.wixproj'
$installers = @()
foreach ($culture in @('en-US', 'ru-RU')) {
    Write-Host "Building $culture MSI $version..."
    & dotnet build $installerProject -c Release -p:Platform=x64 `
        "-p:CoreSource=$CoreDirectory" "-p:PayloadFile=$payload" `
        "-p:MsiVersion=$version" "-p:ReleaseVersion=$ReleaseVersion" `
        "-p:Cultures=$culture" '-p:AcceptWixEula=true' "-p:OutputPath=$staging\msi\" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "MSI build failed: $LASTEXITCODE" }
    [string]$name = "SimpleGit11-$ReleaseVersion-win-x64-$culture.msi"
    [string]$builtMsi = Join-Path $staging "msi\$culture\$name"
    & (Join-Path $PSScriptRoot 'Test-InstallerPackage.ps1') -Path $builtMsi
    [string]$destination = Join-Path $artifactDirectory $name
    Remove-FileUnderRoot -Path $destination -Root $repositoryRoot
    Copy-Item -LiteralPath $builtMsi -Destination $destination
    [string]$checksumPath = "$destination.sha256"
    Assert-NoReparsePointUnderRoot -Path $checksumPath -Root $repositoryRoot | Out-Null
    [string]$hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    Set-Content -LiteralPath $checksumPath -Value "$hash  $name" -Encoding Ascii
    $installers += $destination
}
Write-Warning 'MSI files are unsigned. Sign release artifacts with your production certificate and a timestamp, then regenerate their SHA-256 files.'
return $installers
