#Requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PayloadScript)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. $PayloadScript

function Assert-Rejected {
    param([scriptblock]$Action, [string]$Message)
    try { & $Action }
    catch {
        if ($_.Exception.Message -notlike "*$Message*") { throw }
        return
    }
    throw "Expected rejection: $Message"
}

if ((Get-InstallerVersion '1.2.3') -ne '1.2.3') { throw 'Stable MSI version was changed.' }
if ((Get-InstallerVersion '1.2.3-dev.1' '1.2.2') -ne '1.2.2') { throw 'Explicit test version was not used.' }
foreach ($version in @('1.2.3-dev.1', '1.2.3-preview.2', '1.2.3-rc.1', '1.2.3-dev.local.20260831170523')) {
    if ((Get-InstallerVersion $version) -ne '1.2.3') { throw 'MSI version must default to the application numeric core.' }
}
Assert-Rejected { Get-InstallerVersion '256.0.0-dev.1' } 'exceeds'
Assert-Rejected { Get-InstallerVersion '1.2.3' '1.2.4' } 'must match'
foreach ($invalid in @('256.0.0', '1.256.0', '1.0.65536', '1.2', '1.2.3.4', '01.2.3', '..\outside')) {
    Assert-Rejected { Get-InstallerVersion $invalid } 'version'
}
if ((Get-InstallerId 'C' 'Core/A.dll') -ne (Get-InstallerId 'C' 'core\a.DLL')) { throw 'Component IDs must be stable across case/separators.' }
if ((Get-InstallerId 'C' 'Core/A.dll') -eq (Get-InstallerId 'C' 'Ssh/A.dll')) { throw 'Feature component IDs collide.' }

[string]$testBase = Join-Path ([IO.Path]::GetTempPath()) ('SimpleGit11.Installer.' + [guid]::NewGuid().ToString('N'))
[string]$core = Join-Path $testBase 'core'
[string]$ssh = Join-Path $testBase 'ssh'
[string]$output = Join-Path $testBase 'Payload.wxs'
[string]$link = Join-Path $core 'linked'
try {
    foreach ($folder in @($core, $ssh)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
    foreach ($file in @('SimpleGit11.exe', 'SimpleGit11.dll', 'SimpleGit11.pri', 'App.xbf', 'MainWindow.xbf', 'Assets\AppIcon.ico', 'LICENSE', 'Licenses\PACKAGES.txt', 'nested & folder\inner\test.dll')) {
        [string]$path = Join-Path $core $file
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        Set-Content -LiteralPath $path -Value 'fixture'
    }
    foreach ($file in @('SimpleGit11.Plugin.Ssh.dll', 'SimpleGit11.Plugin.Ssh.deps.json', 'Renci.SshNet.dll', 'BouncyCastle.Cryptography.dll', 'Microsoft.Extensions.Logging.Abstractions.dll')) {
        Set-Content -LiteralPath (Join-Path $ssh $file) -Value 'fixture'
    }
    [string]$repositoryRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PayloadScript))
    @{
        id = 'simplegit11.ssh'
        name = 'SSH'
        version = '1.2.3'
        apiVersion = '1.0'
        minimumHostVersion = '1.0.0'
        entryAssembly = 'SimpleGit11.Plugin.Ssh.dll'
        entryType = 'SimpleGit11.Plugin.Ssh.SshPlugin'
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ssh 'plugin.json')
    Write-InstallerPayload $core $ssh $output $testBase
    [string]$first = Get-Content -LiteralPath $output -Raw
    Write-InstallerPayload $core $ssh $output $testBase
    if ($first -ne (Get-Content -LiteralPath $output -Raw)) { throw 'Payload generation is not deterministic.' }
    [xml]$xml = $first
    $ns = New-Object Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace('w', 'http://wixtoolset.org/schemas/v4/wxs')
    $sshGroup = $xml.SelectSingleNode('//w:ComponentGroup[@Id="SshFiles"]', $ns)
    if ($sshGroup.SelectNodes('.//w:File', $ns).Count -ne 6) { throw 'SSH payload count mismatch.' }
    foreach ($component in $xml.SelectNodes('//w:Component', $ns)) {
        if ($null -eq $component.SelectSingleNode('w:RegistryValue[@Root="HKMU"][@KeyPath="yes"]', $ns)) { throw 'Missing context-aware component key path.' }
    }
    Copy-Item -LiteralPath (Join-Path $ssh 'Renci.SshNet.dll') -Destination $core
    Assert-Rejected { Write-InstallerPayload $core $ssh $output $testBase } 'leaked'
    Remove-Item -LiteralPath (Join-Path $core 'Renci.SshNet.dll')
    New-Item -ItemType Directory -Path (Join-Path $core 'Plugins\Other') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $core 'Plugins\Other\unexpected.dll') -Value 'fixture'
    Assert-Rejected { Write-InstallerPayload $core $ssh $output $testBase } 'leaked'
    Remove-DirectoryUnderRoot -Path (Join-Path $core 'Plugins') -Root $testBase
    Set-Content -LiteralPath (Join-Path $ssh 'SimpleGit11.Extensibility.dll') -Value 'fixture'
    Assert-Rejected { Write-InstallerPayload $core $ssh $output $testBase } 'Unexpected SSH'
    Remove-Item -LiteralPath (Join-Path $ssh 'SimpleGit11.Extensibility.dll')
    New-Item -ItemType Junction -Path $link -Target $ssh | Out-Null
    Assert-Rejected { Write-InstallerPayload $core $ssh $output $testBase } 'reparse point'
    Assert-Rejected { Remove-DirectoryUnderRoot $core $testBase } 'reparse point'
    if (-not (Test-Path -LiteralPath (Join-Path $ssh 'plugin.json'))) { throw 'Junction target was deleted.' }
    [IO.Directory]::Delete($link)
    Remove-Item -LiteralPath (Join-Path $ssh 'Renci.SshNet.dll')
    Assert-Rejected { Write-InstallerPayload $core $ssh $output $testBase } 'missing'

    [xml]$package = Get-Content -LiteralPath (Join-Path $repositoryRoot 'SimpleGit11.Installer\Package.wxs') -Raw
    if ($package.OuterXml -match 'WixUILicenseRtf|WixUI_FeatureTree') {
        throw 'The stock license-acceptance wizard must not be included.'
    }
    [xml]$installerUi = Get-Content -LiteralPath (Join-Path $repositoryRoot 'SimpleGit11.Installer\InstallerUI.wxs') -Raw
    if ($installerUi.OuterXml -match 'LicenseAgreementDlg|LicenseAccepted') {
        throw 'The installer must not display or require license acceptance.'
    }
    foreach ($expected in @(
        @('WelcomeDlg', 'Next', 'SimpleGit11ScopeDlg', 'NOT Installed AND NOT EXISTINGUSERFOLDER AND NOT EXISTINGMACHINEFOLDER'),
        @('WelcomeDlg', 'Next', 'CustomizeDlg', 'NOT Installed AND (EXISTINGUSERFOLDER OR EXISTINGMACHINEFOLDER)'),
        @('SimpleGit11ScopeDlg', 'Next', 'InstallDirDlg', ''),
        @('InstallDirDlg', 'Next', 'CustomizeDlg', ''),
        @('CustomizeDlg', 'Back', 'InstallDirDlg', 'NOT Installed AND NOT EXISTINGUSERFOLDER AND NOT EXISTINGMACHINEFOLDER'),
        @('CustomizeDlg', 'Back', 'WelcomeDlg', 'NOT Installed AND (EXISTINGUSERFOLDER OR EXISTINGMACHINEFOLDER)'),
        @('CustomizeDlg', 'Back', 'MaintenanceTypeDlg', 'Installed')
    )) {
        if (@($installerUi.Wix.Fragment.UI.Publish | Where-Object {
            $_.Dialog -eq $expected[0] -and $_.Control -eq $expected[1] -and $_.GetAttribute('Event') -eq 'NewDialog' -and
            $_.Value -eq $expected[2] -and $_.GetAttribute('Condition') -eq $expected[3]
        }).Count -ne 1) { throw "Invalid installer navigation: $($expected -join ' / ')" }
    }
    foreach ($launch in $package.Wix.Package.Launch) {
        if ($launch.Condition -match '\b(WindowsBuild|VersionNT|VersionNT64)\b') {
            throw 'A compatibility-reported Windows version must not block installation on Windows 11.'
        }
    }
    [xml]$installerProject = Get-Content -LiteralPath (Join-Path $repositoryRoot 'SimpleGit11.Installer\SimpleGit11.Installer.wixproj') -Raw
    if ($installerProject.Project.PropertyGroup.InstallerPlatform -ne 'x64') {
        throw 'Native MSI x64 architecture enforcement must be preserved.'
    }
    $cleanup = $package.Wix.Package.Component.RemoveFolderEx
    if ($cleanup.Property -ne 'SimpleGit11UserDataPath' -or $cleanup.On -ne 'uninstall' -or
        $cleanup.Condition -ne 'Installed AND NOT ALLUSERS AND PURGEUSERDATA = "1" AND REMOVE ~= "ALL" AND NOT UPGRADINGPRODUCTCODE') {
        throw 'Cleanup must require explicit opt-in, complete uninstall and no upgrade.'
    }
    $setDataPath = @($package.Wix.Package.SetProperty | Where-Object Id -eq 'SimpleGit11UserDataPath')[0]
    if ($setDataPath.Value -ne '[LocalAppDataFolder]SimpleGit11' -or $setDataPath.Condition -ne 'NOT ALLUSERS') {
        throw 'Cleanup must resolve only the fixed per-user data folder.'
    }
    if ($package.Wix.Package.Scope -ne 'perUserOrMachine') { throw 'Both install scopes must be supported, per-user by default.' }
    if ($package.Wix.Package.StandardDirectory[0].Id -ne 'ProgramFiles64Folder') { throw 'Use the MSI-redirectable x64 program directory.' }
    $packageNs = New-Object Xml.XmlNamespaceManager($package.NameTable)
    $packageNs.AddNamespace('w', 'http://wixtoolset.org/schemas/v4/wxs')
    foreach ($registry in $package.SelectNodes('//w:RegistryValue | //w:RemoveRegistryKey', $packageNs)) {
        if ($registry.Root -ne 'HKMU') { throw 'Installation registry entries must follow the chosen context.' }
    }
    foreach ($dialog in @('InstallDirDlg', 'BrowseDlg')) {
        if (@($installerUi.Wix.Fragment.UI.Publish | Where-Object {
            $_.Dialog -eq $dialog -and $_.GetAttribute('Event') -eq 'CheckTargetPath'
        }).Count -ne 1) { throw "Directory validation is missing for $dialog." }
    }
    if ($installerUi.Wix.Fragment.UI.Dialog.OuterXml -match 'Privileged|AdminUser') {
        throw 'A standard user must be able to select all-users installation and request elevation.'
    }
    if ($package.Wix.Package.InstallUISequence.Show.Condition -notmatch 'NOT ALLUSERS') {
        throw 'Machine uninstall must not offer deletion of a user profile.'
    }
    $coreFeature = @($package.Wix.Package.Feature | Where-Object Id -eq 'Core')[0]
    if ($coreFeature.AllowAbsent -ne 'no') { throw 'Core must not be removable during feature maintenance.' }
    $english = [xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'SimpleGit11.Installer\Strings\en-US\Resources.wxl') -Raw)
    $russian = [xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'SimpleGit11.Installer\Strings\ru-RU\Resources.wxl') -Raw)
    if (Compare-Object @($english.WixLocalization.String.Id | Sort-Object) @($russian.WixLocalization.String.Id | Sort-Object)) {
        throw 'Installer translations have different resource keys.'
    }
    if (@($english.WixLocalization.String.Id | Where-Object { $_ -in @('WindowsRequired', 'PerUserRequired') }).Count -ne 0) {
        throw 'Removed restrictions must not remain in localization.'
    }
    # Plugin license collection must tolerate optional nuspec metadata and not require host runtimes.
    [string]$licenseFixture = Join-Path $testBase 'license-fixture'
    [string]$packageDirectory = Join-Path $licenseFixture 'packages\fixture\1.0.0'
    [string]$published = Join-Path $licenseFixture 'published'
    New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $published -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $published 'Fixture.dll') -Value 'fixture'
    Set-Content -LiteralPath (Join-Path $packageDirectory 'fixture.nuspec') -Value '<package><metadata><id>Fixture</id><version>1.0.0</version><license type="expression">MIT</license></metadata></package>'
    Set-Content -LiteralPath (Join-Path $licenseFixture 'sources.json') -Value '[]'
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $licenseFixture
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.txt') -Destination $licenseFixture
    $fixtureAssets = @{
        targets = @{ 'net8.0/win-x64' = @{ 'Fixture/1.0.0' = @{ runtime = @{ 'lib/Fixture.dll' = @{} } } } }
        libraries = @{ 'Fixture/1.0.0' = @{ type = 'package'; path = 'fixture/1.0.0' } }
        packageFolders = @{ (Join-Path $licenseFixture 'packages') = @{} }
        project = @{ restore = @{ projectPath = (Join-Path $licenseFixture 'Fixture.csproj') } }
    }
    [string]$assetsPath = Join-Path $licenseFixture 'assets.json'
    $fixtureAssets | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $assetsPath
    & (Join-Path (Split-Path -Parent $PayloadScript) 'Collect-ReleaseLicenses.ps1') `
        -RepositoryRoot $licenseFixture -ProjectAssetsPath $assetsPath `
        -SourceComponentsPath (Join-Path $licenseFixture 'sources.json') -PublishedDirectory $published -Plugin
    if ((Get-Content -LiteralPath (Join-Path $published 'Licenses\PACKAGES.txt') -Raw) -notmatch 'Package: Fixture') {
        throw 'Plugin dependency license was omitted.'
    }
    Write-Host 'Installer payload, versions, localization and cleanup guards passed.'
}
finally {
    if (Test-Path -LiteralPath $link) { [IO.Directory]::Delete($link) }
    if (Test-Path -LiteralPath $testBase) {
        Remove-DirectoryUnderRoot -Path $testBase -Root ([IO.Path]::GetTempPath())
    }
}
