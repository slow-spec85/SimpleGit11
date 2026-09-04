#Requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Path, [string]$WixDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Installer-Payload.ps1')
[string]$fullPath = [IO.Path]::GetFullPath($Path)
if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "MSI not found: $fullPath" }
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $null
try {
    $database = $installer.OpenDatabase($fullPath, 0)
    $summary = $database.SummaryInformation(0)
    try {
        if ($summary.Property(7) -notmatch '^x64;') {
            throw 'The MSI template must enforce x64 architecture.'
        }
    }
    finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary) }
    function Read-MsiRows {
        param([string]$Query, [int]$Columns)
        $view = $database.OpenView($Query)
        try {
            [void]$view.Execute()
            while ($null -ne ($record = $view.Fetch())) {
                try {
                    $row = @()
                    for ($index = 1; $index -le $Columns; $index++) { $row += $record.StringData($index) }
                    ,$row
                }
                finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
            }
        }
        finally {
            [void]$view.Close()
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
        }
    }
    $launchConditions = @(Read-MsiRows 'SELECT `Condition` FROM `LaunchCondition`' 1)
    foreach ($condition in $launchConditions) {
        if ($condition[0] -match '\b(WindowsBuild|VersionNT|VersionNT64)\b') {
            throw 'Do not gate installation on compatibility-reported Windows versions.'
        }
    }
    # Evaluate the actual conditions with legacy compatibility values, without running MSI actions.
    $session = $installer.OpenPackage($fullPath, 1)
    try {
        foreach ($entry in @{ WindowsBuild = '9600'; VersionNT = '603'; VersionNT64 = '603' }.GetEnumerator()) {
            [void]$session.GetType().InvokeMember('Property', [Reflection.BindingFlags]::SetProperty,
                $null, $session, @($entry.Key, $entry.Value))
        }
        foreach ($condition in $launchConditions) {
            if ($session.EvaluateCondition($condition[0]) -ne 1) {
                throw "Launch condition rejected a clean per-user session with compatibility OS values: $($condition[0])"
            }
        }
    }
    finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($session) }
    $dialogs = @(Read-MsiRows 'SELECT `Dialog` FROM `Dialog`' 1)
    if (@($dialogs | Where-Object { $_[0] -match 'LicenseAgreement' }).Count -ne 0) {
        throw 'The license-acceptance page must not be included in the MSI.'
    }
    $events = @(Read-MsiRows 'SELECT `Dialog_`, `Control_`, `Event`, `Argument`, `Condition`, `Ordering` FROM `ControlEvent`' 6 | Sort-Object { [int]$_[5] })
    if (@($events | Where-Object { $_[4] -match 'LicenseAccepted' }).Count -ne 0) {
        throw 'MSI navigation must not require license acceptance.'
    }
    foreach ($expected in @(
        @('WelcomeDlg', 'Next', 'SimpleGit11ScopeDlg', 'NOT Installed AND NOT EXISTINGUSERFOLDER AND NOT EXISTINGMACHINEFOLDER'),
        @('WelcomeDlg', 'Next', 'CustomizeDlg', 'NOT Installed AND (EXISTINGUSERFOLDER OR EXISTINGMACHINEFOLDER)'),
        @('SimpleGit11ScopeDlg', 'Next', 'InstallDirDlg', '1'),
        @('InstallDirDlg', 'Next', 'CustomizeDlg', '1'),
        @('CustomizeDlg', 'Back', 'InstallDirDlg', 'NOT Installed AND NOT EXISTINGUSERFOLDER AND NOT EXISTINGMACHINEFOLDER'),
        @('CustomizeDlg', 'Back', 'WelcomeDlg', 'NOT Installed AND (EXISTINGUSERFOLDER OR EXISTINGMACHINEFOLDER)'),
        @('CustomizeDlg', 'Back', 'MaintenanceTypeDlg', 'Installed'),
        @('MaintenanceTypeDlg', 'ChangeButton', 'CustomizeDlg', '1'),
        @('MaintenanceTypeDlg', 'RepairButton', 'VerifyReadyDlg', '1'),
        @('MaintenanceTypeDlg', 'RemoveButton', 'VerifyReadyDlg', '1')
    )) {
        if (@($events | Where-Object {
            $_[0] -eq $expected[0] -and $_[1] -eq $expected[1] -and $_[2] -eq 'NewDialog' -and
            $_[3] -eq $expected[2] -and $_[4] -eq $expected[3]
        }).Count -ne 1) { throw "Invalid MSI navigation: $($expected -join ' / ')" }
    }
    $features = @(Read-MsiRows 'SELECT `Feature`, `Level` FROM `Feature`' 2)
    foreach ($expected in @(@('Core', '1'), @('Ssh', '2'), @('Desktop', '2'))) {
        if (@($features | Where-Object { $_[0] -eq $expected[0] -and $_[1] -eq $expected[1] }).Count -ne 1) {
            throw "Invalid MSI feature: $($expected[0])"
        }
    }
    $files = @(Read-MsiRows 'SELECT `File`.`FileName`, `FeatureComponents`.`Feature_`, `File`.`File` FROM `File`, `FeatureComponents` WHERE `File`.`Component_` = `FeatureComponents`.`Component_`' 3)
    [string]$applicationLicenseId = Get-InstallerId 'F' 'CoreFiles/LICENSE'
    if (@($files | Where-Object { $_[2] -eq $applicationLicenseId -and $_[1] -eq 'Core' }).Count -ne 1) {
        throw 'Removing the license page must not remove the application license file.'
    }
    foreach ($privateFile in @('SimpleGit11.Plugin.Ssh.dll', 'Renci.SshNet.dll', 'BouncyCastle.Cryptography.dll', 'plugin.json')) {
        $owners = @($files | Where-Object { ($_[0] -split '\|')[-1] -eq $privateFile })
        if ($owners.Count -ne 1 -or $owners[0][1] -ne 'Ssh') { throw "SSH file has invalid MSI ownership: $privateFile" }
    }
    $cleanup = @(Read-MsiRows 'SELECT `Property`, `InstallMode`, `Condition` FROM `Wix4RemoveFolderEx`' 3)
    if ($cleanup.Count -ne 1 -or $cleanup[0][0] -ne 'SimpleGit11UserDataPath' -or $cleanup[0][1] -ne '2' -or
        $cleanup[0][2] -notmatch 'PURGEUSERDATA = "1"' -or $cleanup[0][2] -notmatch 'NOT UPGRADINGPRODUCTCODE' -or
        $cleanup[0][2] -notmatch 'NOT ALLUSERS' -or $cleanup[0][2] -notmatch 'REMOVE ~= "ALL"') { throw 'Unsafe MSI data cleanup configuration.' }
    $sequence = @(Read-MsiRows 'SELECT `Action`, `Sequence` FROM `InstallUISequence`' 2)
    $purge = @($sequence | Where-Object { $_[0] -eq 'PurgeUserDataDlg' })
    $execute = @($sequence | Where-Object { $_[0] -eq 'ExecuteAction' })
    if ($purge.Count -ne 1 -or $execute.Count -ne 1 -or [int]$purge[0][1] -ge [int]$execute[0][1]) {
        throw 'The complete-removal dialog must run before ExecuteAction.'
    }
    $properties = @(Read-MsiRows 'SELECT `Property`, `Value` FROM `Property`' 2)
    foreach ($expected in @(@('ALLUSERS', '2'), @('MSIINSTALLPERUSER', '1'), @('WIXUI_INSTALLDIR', 'INSTALLFOLDER'))) {
        if (@($properties | Where-Object { $_[0] -eq $expected[0] -and $_[1] -eq $expected[1] }).Count -ne 1) {
            throw "Invalid dual-purpose MSI default: $($expected -join '=')"
        }
    }
    $registry = @(Read-MsiRows 'SELECT `Root` FROM `Registry`' 1)
    if (@($registry | Where-Object { $_[0] -ne '-1' }).Count -ne 0) {
        throw 'All component registry keys must follow the install context (HKMU).'
    }
    $directories = @(Read-MsiRows 'SELECT `Directory`, `Directory_Parent` FROM `Directory`' 2)
    if (@($directories | Where-Object { $_[0] -eq 'INSTALLFOLDER' -and $_[1] -eq 'ProgramFiles64Folder' }).Count -ne 1) {
        throw 'INSTALLFOLDER must use the MSI-redirectable ProgramFiles64Folder.'
    }
    $shortcuts = @(Read-MsiRows 'SELECT `Shortcut`, `Directory_`, `Component_`, `Target` FROM `Shortcut`' 4)
    foreach ($expected in @(@('StartMenuShortcut', 'ProgramMenuFolder'), @('DesktopShortcut', 'DesktopFolder'))) {
        if (@($shortcuts | Where-Object {
            $_[0] -eq $expected[0] -and $_[1] -eq $expected[1] -and $_[2] -eq $expected[0] -and
            $_[3] -eq '[INSTALLFOLDER]SimpleGit11.exe'
        }).Count -ne 1) { throw 'Shortcut directories and key paths must both follow MSI context redirection.' }
    }
    if (@($properties | Where-Object { $_[0] -eq 'PURGEUSERDATA' -and $_[1] }).Count -ne 0) {
        throw 'Data deletion must be off by default.'
    }
    $actions = @(Read-MsiRows 'SELECT `Action`, `Type`, `Source`, `Target` FROM `CustomAction`' 4)
    $setDataPath = @($actions | Where-Object { $_[2] -eq 'SimpleGit11UserDataPath' })
    if ($setDataPath.Count -ne 1 -or $setDataPath[0][3] -ne '[LocalAppDataFolder]SimpleGit11') {
        throw 'Data cleanup must use the fixed application data directory.'
    }
    $executeSequence = @(Read-MsiRows 'SELECT `Action`, `Sequence` FROM `InstallExecuteSequence`' 2)
    foreach ($actionSequence in @($sequence, $executeSequence)) {
        [int]$appSearch = @($actionSequence | Where-Object { $_[0] -eq 'AppSearch' })[0][1]
        [int]$restoreScope = @($actionSequence | Where-Object { $_[0] -eq 'RestoreMachineScope' })[0][1]
        [int]$findRelated = @($actionSequence | Where-Object { $_[0] -eq 'FindRelatedProducts' })[0][1]
        [int]$launch = @($actionSequence | Where-Object { $_[0] -eq 'LaunchConditions' })[0][1]
        [int]$costFinalize = @($actionSequence | Where-Object { $_[0] -eq 'CostFinalize' })[0][1]
        if ($appSearch -ge $restoreScope -or $restoreScope -ge $findRelated -or $findRelated -ge $launch) {
            throw 'Scope detection/restoration must precede related-product and launch checks in both sequences.'
        }
        foreach ($restoreAction in @('RestoreUserFolder', 'RestoreMachineFolder')) {
            [int]$restorePath = @($actionSequence | Where-Object { $_[0] -eq $restoreAction })[0][1]
            if ($restorePath -le $appSearch -or $restorePath -ge $costFinalize) { throw 'Restore the saved folder before MSI costing.' }
        }
    }
    $setPathSequence = @($executeSequence | Where-Object { $_[0] -eq $setDataPath[0][0] })
    $removeFoldersSequence = @($executeSequence | Where-Object { $_[0] -eq 'Wix4RemoveFoldersEx_X64' })
    $costSequence = @($executeSequence | Where-Object { $_[0] -eq 'CostInitialize' })
    if ($setPathSequence.Count -ne 1 -or $removeFoldersSequence.Count -ne 1 -or $costSequence.Count -ne 1 -or
        [int]$setPathSequence[0][1] -ge [int]$removeFoldersSequence[0][1] -or
        [int]$removeFoldersSequence[0][1] -ge [int]$costSequence[0][1]) {
        throw 'Data cleanup path must be resolved before folder enumeration and MSI costing.'
    }
    . (Join-Path $PSScriptRoot 'Test-InstallerSession.ps1')
    Assert-InstallerSessionBehavior -Installer $installer -Path $fullPath -Actions $actions -Events $events `
        -LaunchConditions $launchConditions -CleanupCondition $cleanup[0][2]
    if ($WixDirectory) {
        [string[]]$allowedPaths = @(
            'Microsoft.ui.xaml.dll', 'Microsoft.UI.Xaml.Phone.dll',
            'gd-gb\Microsoft.ui.xaml.dll.mui', 'gd-gb\Microsoft.UI.Xaml.Phone.dll.mui',
            'mi-NZ\Microsoft.ui.xaml.dll.mui', 'mi-NZ\Microsoft.UI.Xaml.Phone.dll.mui',
            'ug-CN\Microsoft.ui.xaml.dll.mui', 'ug-CN\Microsoft.UI.Xaml.Phone.dll.mui'
        )
        [string[]]$allowedFileIds = @($allowedPaths | ForEach-Object { Get-InstallerId 'F' ('CoreFiles/' + $_) })
        # Capture both streams without PowerShell interpreting expected native diagnostics as exceptions.
        $startInfo = New-Object Diagnostics.ProcessStartInfo
        $startInfo.FileName = (Get-Command dotnet -ErrorAction Stop).Source
        $startInfo.Arguments = '"{0}" msi validate "{1}" -ice ICE03 -ice ICE57 -acceptEula wix7' -f (Join-Path $WixDirectory 'wix.dll'), $fullPath
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.StandardOutputEncoding = [Text.Encoding]::UTF8
        $startInfo.StandardErrorEncoding = [Text.Encoding]::UTF8
        $process = [Diagnostics.Process]::Start($startInfo)
        try {
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            $process.WaitForExit()
            [string[]]$validation = ($stdout.Result + [Environment]::NewLine + $stderr.Result) -split '\r?\n'
            [int]$validationExitCode = $process.ExitCode
        }
        finally { $process.Dispose() }
        [int]$accepted = 0
        foreach ($line in $validation) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            if ($line -match 'ICE03: (Invalid Language Id|String overflow \(greater than length permitted in column\)); Table: File, Column: Language, Key\(s\): (?<FileId>F[0-9A-F]{32})\s*$' -and
                $Matches.FileId -in $allowedFileIds) {
                $accepted++
            }
            elseif ($line -match "ICE57: Component '(StartMenuShortcut|DesktopShortcut)' has both per-user data and a keypath that can be either per-user or per-machine\.\s*$") {
                # ICE57 predates dual-purpose redirection. Both the shortcut directory
                # and HKMU key path follow ALLUSERS; their composition is checked above.
                $accepted++
            }
            else { throw "Unexpected MSI validation diagnostic: $line" }
        }
        if ($validationExitCode -ne 0 -and $accepted -eq 0) { throw "MSI ICE03/ICE57 validation failed: $validationExitCode" }
        Write-Host "ICE03/ICE57 validated; $accepted known language metadata and dual-purpose shortcut exceptions."
    }
    Write-Host "MSI composition and cleanup guards verified: $fullPath"
}
finally {
    if ($null -ne $database) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
