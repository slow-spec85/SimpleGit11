# Use an already-open local app on the History page with a commit and file selected.
# Pass -CommitRange to run on a commit-range page with a commit list.
# Run at or above 1060 effective pixels, then below 1060 with -Narrow.
# The initial visibility must be at its defaults. The app is never launched by this script.
param(
    [Parameter(Mandatory)][ValidateRange(1, [int]::MaxValue)][int]$AppPid,
    [switch]$Narrow,
    [switch]$CommitRange
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$window = (Get-Process -Id $AppPid).MainWindowHandle
if (-not $window) { throw 'The app must already have an open window.' }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($window)
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')

function Invoke-HistoryUi([string[]]$Arguments) {
    $output = @(& winapp ui @Arguments -a $AppPid)
    if ($LASTEXITCODE -ne 0) { throw ($output -join "`n") }
}

function Find-Control([string]$Id) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Get-ControlBounds([string]$Id) {
    $element = Find-Control $Id
    if (-not $element -or $element.Current.IsOffscreen) { throw "Control is not visible: $Id" }
    $element.Current.BoundingRectangle
}

function Assert-Panes([bool]$Commits, [bool]$Files, [bool]$Details, [bool]$Diff) {
    $panes = [ordered]@{
        HistoryCommitsListView = $Commits
        HistoryChangedFilesTitle = $Files
        HistoryCommitDetailsTitle = $Details
        DiffViewerSearchToggle = $Diff
    }
    foreach ($selector in $panes.Keys) {
        if ($panes[$selector]) {
            Invoke-HistoryUi @('wait-for', $selector, '--property', 'IsOffscreen', '--value', 'False', '-t', '5000')
        } else {
            Invoke-HistoryUi @('wait-for', $selector, '--gone', '-t', '5000')
        }
    }
}

$viewButtons = @('HistoryCommitListToggleButton', 'HistoryFileListToggleButton',
    'HistoryCommitDetailsToggleButton', 'HistoryDiffToggleButton')
$initialPanesConfirmed = $false
try {
    Assert-Panes $true $true $false $true
    $initialPanesConfirmed = $true
    $oldToggle = Find-Control 'CommitDetailsToggleButton'
    if ($oldToggle -and -not $oldToggle.Current.IsOffscreen) {
        throw 'The commit list still has its old details toggle.'
    }

    $titleId = if ($CommitRange) { 'CommitRangePageTitle' } else { 'HistoryPageTitle' }
    $actionsId = if ($CommitRange) { 'CommitRangeActionCommands' } else { 'HistoryActionCommands' }
    $title = Get-ControlBounds $titleId
    $firstToggle = Get-ControlBounds $viewButtons[0]
    $lastToggle = Get-ControlBounds $viewButtons[3]
    $scale = $firstToggle.Height / 48
    if ([Math]::Abs($firstToggle.Width - 40 * $scale) -gt 2) {
        throw 'The toggle bounds must account for the native insets of the square button face.'
    }
    $toolbarRight = $lastToggle.Right
    $actionBar = Find-Control $actionsId
    if (-not $CommitRange -or ($actionBar -and -not $actionBar.Current.IsOffscreen)) {
        $actions = Get-ControlBounds $actionsId
        $separatorGap = $actions.Left - $lastToggle.Right
        if ($separatorGap -lt (8 * $scale) -or $separatorGap -gt (26 * $scale) -or
            $lastToggle.Bottom -le $actions.Top -or $actions.Bottom -le $lastToggle.Top) {
            throw 'The two groups must sit side by side with a gap for the separator.'
        }
        $toolbarRight = $actions.Right
    }
    $rightMargin = $root.Current.BoundingRectangle.Right - $toolbarRight
    if ($rightMargin -lt 0 -or $rightMargin -gt (56 * $scale)) {
        throw 'The toolbar must align to the right edge of the page.'
    }
    if ($Narrow) {
        if ($firstToggle.Top -le $title.Bottom) {
            throw 'In narrow layout, both groups must move below the heading.'
        }
    } elseif ($firstToggle.Left -lt $title.Right) {
        throw 'In wide layout, the toggles must sit to the right of the heading.'
    }

    Invoke-HistoryUi @('invoke', $viewButtons[0], '--action', 'toggle-off')
    Assert-Panes $false $true $false $true
    Invoke-HistoryUi @('invoke', $viewButtons[1], '--action', 'toggle-off')
    Assert-Panes $false $false $false $true
    Invoke-HistoryUi @('invoke', $viewButtons[3], '--action', 'toggle-off')
    Assert-Panes $false $false $false $false
    Invoke-HistoryUi @('invoke', $viewButtons[2], '--action', 'toggle-on')
    Assert-Panes $false $false $true $false

    if (-not $CommitRange) {
        Invoke-HistoryUi @('invoke', 'BranchesNavigationItem', '--action', 'select')
        Invoke-HistoryUi @('wait-for', $viewButtons[0], '--gone', '-t', '5000')
        Invoke-HistoryUi @('invoke', 'HistoryNavigationItem', '--action', 'select')
        Assert-Panes $false $false $true $false
    }
} finally {
    try {
        if ($initialPanesConfirmed) {
            if (-not $CommitRange) {
                Invoke-HistoryUi @('invoke', 'HistoryNavigationItem', '--action', 'select')
            }
            Invoke-HistoryUi @('wait-for', $viewButtons[0], '-t', '5000')
            Invoke-HistoryUi @('invoke', $viewButtons[0], '--action', 'toggle-on')
            Invoke-HistoryUi @('invoke', $viewButtons[1], '--action', 'toggle-on')
            Invoke-HistoryUi @('invoke', $viewButtons[2], '--action', 'toggle-off')
            Invoke-HistoryUi @('invoke', $viewButtons[3], '--action', 'toggle-on')
        }
    } finally {
        try { & winapp ui yield | Out-Null }
        finally { $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow }
    }
}

Assert-Panes $true $true $false $true
Write-Output 'History pane visibility and header layout passed.'
