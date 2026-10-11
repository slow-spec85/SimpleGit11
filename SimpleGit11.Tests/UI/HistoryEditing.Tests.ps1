# Run against an already-open app on the History page of a disposable repository.
# The fixture needs two adjacent linear commits and an older commit outside the range.
# No application is launched and Apply is never invoked: all changes remain drafts.
param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$OldestEditableHash,
    [Parameter(Mandatory)][string]$NewestEditableHash,
    [Parameter(Mandatory)][string]$OutsideRangeHash,
    [string]$MergeHash = "",
    # Optional tagged commit; OutsideRangeHash must be untagged for this check.
    [string]$TaggedHash = ""
)

$ErrorActionPreference = 'Stop'
$taskUiResults = [Collections.Generic.List[object]]::new()
function Invoke-HistoryUi([string[]]$Arguments) {
    $taskOutput = @(& winapp ui @Arguments -a $AppPid)
    if ($LASTEXITCODE -ne 0) { throw ($taskOutput -join "`n") }
}
function Assert-HistoryUi([string]$Selector, [string]$Property, [string]$Value) {
    Invoke-HistoryUi @('wait-for', $Selector, '--property', $Property, '--value', $Value, '-t', '5000')
}
function Test-HistoryUi([string]$Name, [scriptblock]$Action) {
    try {
        & $Action | Out-Null
        $taskUiResults.Add(@{ name = $Name; status = 'PASS' })
    } catch {
        $taskUiResults.Add(@{ name = $Name; status = 'FAIL'; detail = "$_" })
        throw
    }
}

try {
    if ($TaggedHash) {
        Test-HistoryUi 'Tags only filters commits and reset restores the full list' {
            Invoke-HistoryUi @('invoke', 'CommitFilterToggleButton')
            Invoke-HistoryUi @('invoke', 'TagsOnlyToggleSwitch')
            Invoke-HistoryUi @('send-keys', 'escape')
            Invoke-HistoryUi @('wait-for', "HistoryCommit-$TaggedHash")
            Invoke-HistoryUi @('wait-for', "HistoryCommit-$OutsideRangeHash", '--gone')
            Invoke-HistoryUi @('invoke', 'CommitFilterToggleButton')
            Invoke-HistoryUi @('invoke', 'ResetCommitFiltersButton')
            Invoke-HistoryUi @('send-keys', 'escape')
            Invoke-HistoryUi @('wait-for', "HistoryCommit-$OutsideRangeHash")
        }
    }
    Test-HistoryUi 'Quick merge and delete are available and can be canceled without starting a draft' {
        Invoke-HistoryUi @('invoke', "HistoryCommit-$OldestEditableHash")
        Invoke-HistoryUi @('send-keys', 'shift+f10', '--target', "HistoryCommit-$OldestEditableHash", '--via', 'send-input')
        Assert-HistoryUi 'HistoryQuickMergeMenuFlyoutItem' 'IsEnabled' 'True'
        Assert-HistoryUi 'HistoryQuickDeleteMenuFlyoutItem' 'IsEnabled' 'True'
        Invoke-HistoryUi @('invoke', 'HistoryQuickMergeMenuFlyoutItem')
        Invoke-HistoryUi @('wait-for', 'CommitMessageTextBox')
        Invoke-HistoryUi @('invoke', 'CloseButton')
        Invoke-HistoryUi @('wait-for', 'HistoryEditCancelButton', '--gone')
        Invoke-HistoryUi @('send-keys', 'shift+f10', '--target', "HistoryCommit-$OldestEditableHash", '--via', 'send-input')
        Invoke-HistoryUi @('invoke', 'HistoryQuickDeleteMenuFlyoutItem')
        Invoke-HistoryUi @('wait-for', 'CloseButton')
        Invoke-HistoryUi @('invoke', 'CloseButton')
        Assert-HistoryUi "HistoryCommit-$OldestEditableHash" 'IsEnabled' 'True'
    }
    Test-HistoryUi 'Earlier commit message can be edited from the message field menu' {
        Invoke-HistoryUi @('send-keys', 'shift+f10', '--target', 'HistoryCommitMessageTextBox', '--via', 'send-input')
        Assert-HistoryUi 'EditCommitMessageContextButton' 'IsEnabled' 'True'
        Invoke-HistoryUi @('invoke', 'EditCommitMessageContextButton')
        Invoke-HistoryUi @('wait-for', 'CommitMessageTextBox')
        Invoke-HistoryUi @('invoke', 'CloseButton')
        Assert-HistoryUi 'CommitFilterToggleButton' 'IsEnabled' 'True'
        Invoke-HistoryUi @('wait-for', 'HistoryEditCancelButton', '--gone')
    }
    Test-HistoryUi 'Start draft, disable search and hide outside rows' {
        Invoke-HistoryUi @('wait-for', 'StartHistoryEditButton')
        Invoke-HistoryUi @('invoke', "HistoryCommit-$OldestEditableHash")
        Invoke-HistoryUi @('invoke', 'StartHistoryEditButton')
        Invoke-HistoryUi @('wait-for', 'HistoryEditCancelButton')
        Assert-HistoryUi 'CommitFilterToggleButton' 'IsEnabled' 'False'
        Invoke-HistoryUi @('wait-for', "HistoryCommit-$OutsideRangeHash", '--gone')
        Assert-HistoryUi 'HistoryEditApplyButton' 'IsEnabled' 'False'
    }
    Test-HistoryUi 'Moving updates draft and shows both content banners' {
        Invoke-HistoryUi @('invoke', "HistoryCommit-$NewestEditableHash")
        Invoke-HistoryUi @('invoke', 'HistoryEditMoveDownButton')
        Assert-HistoryUi 'HistoryEditApplyButton' 'IsEnabled' 'True'
        Invoke-HistoryUi @('wait-for', 'HistoryDraftFilesInfoBar')
        Invoke-HistoryUi @('wait-for', 'HistoryDraftDiffInfoBar')
        Invoke-HistoryUi @('invoke', 'HistoryEditUndoButton')
        Assert-HistoryUi 'HistoryEditApplyButton' 'IsEnabled' 'False'
    }
    Test-HistoryUi 'Deleted row remains selectable and can be restored' {
        Invoke-HistoryUi @('invoke', "HistoryCommit-$NewestEditableHash")
        Invoke-HistoryUi @('invoke', 'HistoryEditDeleteButton')
        Assert-HistoryUi "HistoryCommit-$NewestEditableHash" 'IsEnabled' 'True'
        Assert-HistoryUi 'HistoryEditMoveDownButton' 'IsEnabled' 'False'
        Assert-HistoryUi 'HistoryEditApplyButton' 'IsEnabled' 'True'
        Invoke-HistoryUi @('invoke', 'HistoryEditDeleteButton')
        Assert-HistoryUi 'HistoryEditApplyButton' 'IsEnabled' 'False'
    }
    Test-HistoryUi 'Canceling combine message keeps the draft unchanged' {
        Invoke-HistoryUi @('invoke', "HistoryCommit-$NewestEditableHash")
        Invoke-HistoryUi @('invoke', 'HistoryEditMergeButton')
        Invoke-HistoryUi @('wait-for', 'CommitMessageTextBox')
        Invoke-HistoryUi @('invoke', 'CloseButton')
        Assert-HistoryUi 'HistoryEditApplyButton' 'IsEnabled' 'False'
    }
    Test-HistoryUi 'Combined row stays visible and cannot be edited independently' {
        Invoke-HistoryUi @('invoke', "HistoryCommit-$NewestEditableHash")
        Invoke-HistoryUi @('invoke', 'HistoryEditMergeButton')
        Invoke-HistoryUi @('wait-for', 'CommitMessageTextBox')
        Invoke-HistoryUi @('invoke', 'PrimaryButton')
        Invoke-HistoryUi @('wait-for', 'CommitMessageTextBox', '--gone')
        Invoke-HistoryUi @('invoke', "HistoryCommit-$NewestEditableHash")
        Assert-HistoryUi "HistoryCommit-$NewestEditableHash" 'IsEnabled' 'True'
        Assert-HistoryUi 'HistoryEditMoveDownButton' 'IsEnabled' 'False'
        Assert-HistoryUi 'HistoryEditDeleteButton' 'IsEnabled' 'False'
        Invoke-HistoryUi @('invoke', 'HistoryEditUndoButton')
        Assert-HistoryUi 'HistoryEditApplyButton' 'IsEnabled' 'False'
    }
    Test-HistoryUi 'Cancel restores normal history and search' {
        Invoke-HistoryUi @('invoke', 'HistoryEditCancelButton')
        Assert-HistoryUi 'CommitFilterToggleButton' 'IsEnabled' 'True'
        Assert-HistoryUi "HistoryCommit-$OutsideRangeHash" 'IsEnabled' 'True'
        Invoke-HistoryUi @('wait-for', 'HistoryEditCancelButton', '--gone')
    }
    Test-HistoryUi 'Shift selects a bounded range' {
        Invoke-HistoryUi @('invoke', "HistoryCommit-$NewestEditableHash")
        Invoke-HistoryUi @('send-keys', 'shift+down', '--target', "HistoryCommit-$NewestEditableHash", '--via', 'send-input')
        Invoke-HistoryUi @('invoke', 'StartHistoryEditButton')
        Invoke-HistoryUi @('wait-for', "HistoryCommit-$OutsideRangeHash", '--gone')
        Assert-HistoryUi "HistoryCommit-$NewestEditableHash" 'IsEnabled' 'True'
        Assert-HistoryUi "HistoryCommit-$OldestEditableHash" 'IsEnabled' 'True'
        Invoke-HistoryUi @('invoke', 'HistoryEditCancelButton')
    }
    Test-HistoryUi 'Mouse drag selects a range and cancel preserves it' {
        Invoke-HistoryUi @('drag', "HistoryCommit-$NewestEditableHash", "HistoryCommit-$OldestEditableHash")
        Invoke-HistoryUi @('invoke', 'StartHistoryEditButton')
        Invoke-HistoryUi @('wait-for', "HistoryCommit-$OutsideRangeHash", '--gone')
        Invoke-HistoryUi @('invoke', 'HistoryEditCancelButton')
        Assert-HistoryUi 'StartHistoryEditButton' 'IsEnabled' 'True'
        # Reopening must retain the bounded range, rather than include older commits.
        Invoke-HistoryUi @('invoke', 'StartHistoryEditButton')
        Invoke-HistoryUi @('wait-for', "HistoryCommit-$OutsideRangeHash", '--gone')
        Invoke-HistoryUi @('invoke', 'HistoryEditCancelButton')
        Invoke-HistoryUi @('click', "HistoryCommit-$OutsideRangeHash")
        Assert-HistoryUi 'StartHistoryEditButton' 'IsEnabled' 'True'
        Invoke-HistoryUi @('drag', "HistoryCommit-$OldestEditableHash", "HistoryCommit-$NewestEditableHash")
        Invoke-HistoryUi @('invoke', 'StartHistoryEditButton')
        Invoke-HistoryUi @('wait-for', "HistoryCommit-$OutsideRangeHash", '--gone')
        Invoke-HistoryUi @('invoke', 'HistoryEditCancelButton')
    }
    if ($MergeHash) {
        Test-HistoryUi 'Cancel parent choices before moving or combining across a merge' {
            Invoke-HistoryUi @('invoke', "HistoryCommit-$OutsideRangeHash")
            Invoke-HistoryUi @('invoke', 'StartHistoryEditButton')
            Invoke-HistoryUi @('invoke', "HistoryCommit-$OldestEditableHash")
            Invoke-HistoryUi @('invoke', 'HistoryEditMoveDownButton')
            Invoke-HistoryUi @('wait-for', 'HistoryEditParentOptions')
            Invoke-HistoryUi @('invoke', 'CloseButton')
            Assert-HistoryUi 'HistoryEditApplyButton' 'IsEnabled' 'False'
            Invoke-HistoryUi @('invoke', "HistoryCommit-$MergeHash")
            Invoke-HistoryUi @('invoke', 'HistoryEditMergeButton')
            Invoke-HistoryUi @('wait-for', 'HistoryEditParentOptions')
            Invoke-HistoryUi @('invoke', 'CloseButton')
            Assert-HistoryUi 'HistoryEditApplyButton' 'IsEnabled' 'False'
            Invoke-HistoryUi @('invoke', 'HistoryEditCancelButton')
        }
    }
} catch {
    # Best effort: leave no draft behind, including when an assertion failed.
    & winapp ui invoke HistoryEditCancelButton -a $AppPid 2>$null | Out-Null
} finally {
    $taskUiResults | ConvertTo-Json -Depth 4
}
if (@($taskUiResults | Where-Object status -eq 'FAIL').Count -gt 0) { exit 1 }
