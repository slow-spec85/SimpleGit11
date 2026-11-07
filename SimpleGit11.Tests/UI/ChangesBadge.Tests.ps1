# Use Windows PowerShell on the local desktop with an already-open app and a clean test repository.
# The script creates and removes one untracked file. It does not launch the app.
param(
    [Parameter(Mandatory)][ValidateRange(1, [int]::MaxValue)][int]$AppPid,
    [Parameter(Mandatory)][string]$RepositoryPath,
    [ValidateRange(1, 300)][int]$PollIntervalSeconds = 10
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class ChangesBadgeWindow {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
}
"@

$repository = (Resolve-Path -LiteralPath $RepositoryPath).ProviderPath
$status = git -C $repository --no-optional-locks status --porcelain=v1
if ($LASTEXITCODE -ne 0 -or $status) { throw 'Use a clean test repository.' }

$app = Get-Process -Id $AppPid
$window = $app.MainWindowHandle
if (-not $window -or $app.ProcessName -ne 'SimpleGit11' -or [ChangesBadgeWindow]::IsIconic($window)) {
    throw 'Open the test repository in a visible SimpleGit11 window first.'
}
$originalTitle = $app.MainWindowTitle
$countSuffix = " $([char]0x00b7) 1"
$expectedTitle = $originalTitle + $countSuffix
$file = Join-Path $repository "changes-badge-test-$([guid]::NewGuid().ToString('N')).txt"
$fileCreated = $false

function Wait-WindowTitle([string]$Expected) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $app.Refresh()
        if ($app.MainWindowTitle -eq $Expected) { return }
        Start-Sleep -Milliseconds 250
    } while ($timer.Elapsed.TotalSeconds -lt $PollIntervalSeconds + 10)
    throw "Expected window title '$Expected', found '$($app.MainWindowTitle)'."
}

function Get-ChangesItem {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($window)
    $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'ChangesNavigationItem'))
}

try {
    [ChangesBadgeWindow]::ShowWindow($window, 6) | Out-Null
    $stream = [IO.File]::Open($file, [IO.FileMode]::CreateNew)
    $fileCreated = $true
    $stream.Dispose()
    Wait-WindowTitle $expectedTitle
    if (-not [ChangesBadgeWindow]::IsIconic($window)) { throw 'The window must remain minimized during polling.' }

    [ChangesBadgeWindow]::ShowWindow($window, 9) | Out-Null
    $item = Get-ChangesItem
    if (-not $item -or $item.Current.Name -notlike "*$countSuffix") { throw 'Expected one change in navigation.' }
    ([System.Windows.Automation.SelectionItemPattern]$item.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    if ((Get-ChangesItem).Current.Name -notlike "*$countSuffix") { throw 'Viewing changes cleared the count.' }

    Remove-Item -LiteralPath $file
    $fileCreated = $false
    Wait-WindowTitle $originalTitle
    if ((Get-ChangesItem).Current.Name -like "*$countSuffix") { throw 'Navigation kept the count after the tree became clean.' }
    'PASS: background polling updates the title and navigation until the working tree is clean.'
} finally {
    if ($fileCreated) { Remove-Item -LiteralPath $file }
    [ChangesBadgeWindow]::ShowWindow($window, 9) | Out-Null
}
