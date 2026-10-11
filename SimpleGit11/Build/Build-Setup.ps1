#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$MsiPath,
    [Parameter(Mandatory)][string]$ReleaseVersion,
    [Parameter(Mandatory)][string]$InstallerVersion,
    [switch]$AcceptWixEula
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Installer-Payload.ps1')

if (-not $AcceptWixEula) {
    throw 'Read https://docs.firegiant.com/wix/osmf/ and explicitly pass -AcceptWixEula.'
}

[string]$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
[string]$artifactDirectory = Join-Path $repositoryRoot 'artifacts'
[string]$setupDirectory = Join-Path $artifactDirectory '.setup-staging-win-x64'
[string]$bundleDirectory = Join-Path $artifactDirectory '.bundle-staging-win-x64'
[string]$setupProject = Join-Path $repositoryRoot 'SimpleGit11.Setup\SimpleGit11.Setup.csproj'
[string]$bundleProject = Join-Path $repositoryRoot 'SimpleGit11.Setup\Bundle\SimpleGit11.Setup.Bundle.wixproj'
$MsiPath = [IO.Path]::GetFullPath($MsiPath)
Assert-NoReparsePointUnderRoot -Path $MsiPath -Root $repositoryRoot | Out-Null
if (-not (Test-Path -LiteralPath $MsiPath -PathType Leaf)) {
    throw "MSI payload was not found: $MsiPath"
}

Remove-DirectoryUnderRoot -Path $setupDirectory -Root $repositoryRoot
Remove-DirectoryUnderRoot -Path $bundleDirectory -Root $repositoryRoot
New-Item -ItemType Directory -Path $setupDirectory, $bundleDirectory | Out-Null

Write-Host 'Publishing self-contained WinUI setup interface...'
& dotnet publish $setupProject -c Release -p:Platform=x64 -r win-x64 --self-contained true `
    '-p:AcceptEula=wix7' "-p:PublishDir=$setupDirectory\" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Setup publish failed: $LASTEXITCODE" }
Assert-NoReparsePointsInTree -Path $setupDirectory -Root $repositoryRoot | Out-Null
[string]$setupExe = Join-Path $setupDirectory 'SimpleGit11.Setup.exe'
foreach ($requiredFile in @('SimpleGit11.Setup.exe', 'SimpleGit11.Setup.pri', 'App.xbf', 'MainWindow.xbf', 'Assets\AppIcon.ico', 'Assets\MainLogo.png')) {
    if (-not (Test-Path -LiteralPath (Join-Path $setupDirectory $requiredFile) -PathType Leaf)) {
        throw "Setup publish file is missing: $requiredFile"
    }
}

Write-Host 'Building WiX Burn bundle...'
& dotnet build $bundleProject -c Release -p:Platform=x64 `
    '-p:AcceptWixEula=true' '-p:AcceptEula=wix7' `
    "-p:MsiVersion=$InstallerVersion" "-p:ReleaseVersion=$ReleaseVersion" `
    "-p:MsiPath=$MsiPath" "-p:SetupDirectory=$setupDirectory" `
    "-p:SetupExe=$setupExe" "-p:OutputPath=$bundleDirectory\" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Bundle build failed: $LASTEXITCODE" }

[string]$name = "SimpleGit11-$ReleaseVersion-win-x64-setup.exe"
[string]$builtBundle = Join-Path $bundleDirectory $name
if (-not (Test-Path -LiteralPath $builtBundle -PathType Leaf)) {
    throw "Bundle was not produced: $builtBundle"
}
[string]$destination = Join-Path $artifactDirectory $name
Remove-FileUnderRoot -Path $destination -Root $repositoryRoot
Copy-Item -LiteralPath $builtBundle -Destination $destination
[string]$checksumPath = "$destination.sha256"
Assert-NoReparsePointUnderRoot -Path $checksumPath -Root $repositoryRoot | Out-Null
[string]$hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
Set-Content -LiteralPath $checksumPath -Value "$hash  $name" -Encoding Ascii
Write-Warning 'The setup EXE is unsigned. Sign it with a production certificate and a timestamp, then regenerate its SHA-256 file.'
return $destination
