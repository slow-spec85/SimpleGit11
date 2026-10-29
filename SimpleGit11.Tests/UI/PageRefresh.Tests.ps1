# Read-only regression check against an already-running LOCAL app on the History page.
# Use a history with enough commits to scroll and a selected commit with at least two changed files.
# Optional -FocusTargetPid also checks two focus returns without repository changes.
# Enable Refresh when returning to the window beforehand and use another visible LOCAL window.
# Never launches the application or performs Git mutations.
param(
    [Parameter(Mandatory)][ValidateRange(1,[int]::MaxValue)][int]$AppPid,
    [string]$ResultsPath = (Join-Path $env:TEMP 'SimpleGit11-PageRefresh-results.json'),
    [ValidateRange(1,[int]::MaxValue)][int]$FocusTargetPid
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$appProcess = Get-Process -Id $AppPid
if (-not $appProcess.MainWindowHandle) { throw 'The app must already have an open local window.' }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($appProcess.MainWindowHandle)
$scope = [System.Windows.Automation.TreeScope]::Descendants
$results = [Collections.Generic.List[object]]::new()
function Find-Control([string]$Id) {
    $root.FindFirst($scope,[System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,$Id))
}
function Select-Page([string]$Id) {
    $item = Find-Control $Id
    if (-not $item) { throw "Navigation element not found: $Id" }
    ([System.Windows.Automation.SelectionItemPattern]$item.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
}
function Wait-Refresh([string]$ButtonId,[scriptblock]$Action) {
    $timer=[Diagnostics.Stopwatch]::StartNew()
    $wasBusy=$false
    & $Action
    do {
        $button = Find-Control $ButtonId
        if ($button) {
            if (-not $button.Current.IsEnabled) { $wasBusy=$true }
            elseif ($wasBusy) { Start-Sleep -Milliseconds 150; return }
        }
        Start-Sleep -Milliseconds 20
    } while ($timer.Elapsed.TotalSeconds -lt 20)
    throw "Refresh did not become idle: $ButtonId (busy observed: $wasBusy)"
}
function Selected-File {
    $list=Find-Control 'HistoryChangedFilesListView'
    $selection=([System.Windows.Automation.SelectionPattern]$list.GetCurrentPattern(
        [System.Windows.Automation.SelectionPattern]::Pattern)).Current.GetSelection()
    if ($selection.Count -ne 1) { throw 'Expected exactly one selected changed file.' }
    @($selection[0].FindAll($scope,[System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Text)) |
        ForEach-Object { $_.Current.Name }) -join '|'
}
try {
    $files=Find-Control 'HistoryChangedFilesListView'
    $commits=Find-Control 'HistoryCommitsListView'
    if (-not $files -or -not $commits) { throw 'Open the History page with a loaded commit first.' }
    $rows=$files.FindAll([System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem))
    if ($rows.Count -lt 2) { throw 'Select a commit with at least two changed files.' }
    ([System.Windows.Automation.ScrollItemPattern]$rows[1].GetCurrentPattern(
        [System.Windows.Automation.ScrollItemPattern]::Pattern)).ScrollIntoView()
    ([System.Windows.Automation.SelectionItemPattern]$rows[1].GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    $scroll=[System.Windows.Automation.ScrollPattern]$commits.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if (-not $scroll.Current.VerticallyScrollable) { throw 'Use a history large enough to scroll.' }
    $scroll.SetScrollPercent(-1,25)
    Start-Sleep -Milliseconds 500
    $expectedFile=Selected-File
    $expectedScroll=$scroll.Current.VerticalScrollPercent
    Wait-Refresh 'RefreshHistoryButton' {
        ([System.Windows.Automation.InvokePattern](Find-Control 'RefreshHistoryButton').GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    }
    if ((Selected-File) -ne $expectedFile) { throw 'Manual refresh changed the selected file.' }
    if ([Math]::Abs($scroll.Current.VerticalScrollPercent-$expectedScroll) -gt 1) { throw 'Manual refresh moved the history viewport.' }
    $results.Add(@{name='Refresh preserves file and history viewport';status='PASS'})
    Wait-Refresh 'RefreshBranchesButton' { Select-Page 'BranchesNavigationItem' }
    Wait-Refresh 'RefreshHistoryButton' { Select-Page 'HistoryNavigationItem' }
    if ((Selected-File) -ne $expectedFile) { throw 'Navigation changed the selected file.' }
    $commits=Find-Control 'HistoryCommitsListView'
    $scroll=[System.Windows.Automation.ScrollPattern]$commits.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if ([Math]::Abs($scroll.Current.VerticalScrollPercent-$expectedScroll) -gt 1) { throw 'Navigation moved the history viewport.' }
    $results.Add(@{name='Navigation preserves file and history viewport';status='PASS'})
    if ($PSBoundParameters.ContainsKey('FocusTargetPid')) {
        $focusProcess = Get-Process -Id $FocusTargetPid
        if ($FocusTargetPid -eq $AppPid -or -not $focusProcess.MainWindowHandle) {
            throw 'FocusTargetPid must identify another visible local application window.'
        }
        if (-not ('PageRefreshForeground' -as [type])) {
            Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class PageRefreshForeground {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
"@
        }
        function Set-Foreground([IntPtr]$Window) {
            [PageRefreshForeground]::SetForegroundWindow($Window) | Out-Null
            if ([PageRefreshForeground]::GetForegroundWindow() -ne $Window) {
                throw 'Windows did not grant foreground activation. Run on an unlocked desktop.'
            }
        }
        Set-Foreground $appProcess.MainWindowHandle
        for ($activation = 1; $activation -le 2; $activation++) {
            Set-Foreground $focusProcess.MainWindowHandle
            # Exceed both the 5-second inactive threshold and the 10-second refresh cooldown.
            Start-Sleep -Milliseconds 10200
            Wait-Refresh 'RefreshHistoryButton' { Set-Foreground $appProcess.MainWindowHandle }
            if ((Selected-File) -ne $expectedFile) { throw 'Focus refresh changed the selected file.' }
            $commits=Find-Control 'HistoryCommitsListView'
            $scroll=[System.Windows.Automation.ScrollPattern]$commits.GetCurrentPattern(
                [System.Windows.Automation.ScrollPattern]::Pattern)
            if ([Math]::Abs($scroll.Current.VerticalScrollPercent-$expectedScroll) -gt 1) {
                throw 'Focus refresh moved the history viewport.'
            }
            $results.Add(@{name="Focus return $activation refreshes unchanged data and preserves selection and viewport";status='PASS'})
        }
    }
} catch {
    $results.Add(@{name='History refresh regression';status='FAIL';detail="$_"})
} finally {
    @{appPid=$AppPid;results=@($results.ToArray())} | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $ResultsPath -Encoding utf8
}
$results | Format-Table -AutoSize
if (@($results | Where-Object status -eq 'FAIL').Count) { exit 1 }
