# Run against an existing app with the Settings page open. This script never launches the app.
param(
    [Parameter(Mandatory)][ValidateSet('sandbox', 'local')][string]$Target,
    [Parameter(Mandatory)][ValidateRange(1, [int]::MaxValue)][int]$AppPid,
    [Parameter(Mandatory)][ValidateSet('sandbox', 'local')][string]$AppScope,
    [Parameter(Mandatory)][ValidateSet('On', 'Off')][string]$InitialState
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if ($AppScope -ne $Target) { throw 'AppScope must match the target of the existing app process.' }
$scopeArgs = @(if ($Target -eq 'sandbox') { '--on'; 'sandbox' })
$toggleId = 'RefreshOnWindowActivationToggle'
$oppositeState = if ($InitialState -eq 'On') { 'Off' } else { 'On' }
$originalStateVerified = $false
$previousWorkflowId = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')

function Invoke-SettingsUi([string[]]$Arguments) {
    $global:LASTEXITCODE = $null
    $output = @(& winapp ui @Arguments @scopeArgs -a $AppPid)
    if ($null -eq $LASTEXITCODE -or $LASTEXITCODE -ne 0) {
        throw "winapp ui failed: $($output -join "`n")"
    }
    $output
}

try {
    Invoke-SettingsUi @('wait-for', $toggleId, '--property', 'ToggleState', '--value', $InitialState) | Out-Null
    $originalStateVerified = $true
    Invoke-SettingsUi @('scroll-into-view', $toggleId) | Out-Null
    Invoke-SettingsUi @('invoke', $toggleId, '--action', 'toggle') | Out-Null
    Invoke-SettingsUi @('wait-for', $toggleId, '--property', 'ToggleState', '--value', $oppositeState) | Out-Null
    Invoke-SettingsUi @('send-keys', 'space', '--target', $toggleId, '--via', 'send-input') | Out-Null
    Invoke-SettingsUi @('wait-for', $toggleId, '--property', 'ToggleState', '--value', $InitialState) | Out-Null
    @{ name = 'Focus refresh setting supports toggle and keyboard input'; status = 'PASS' } | ConvertTo-Json
} finally {
    try {
        if ($originalStateVerified) {
            Invoke-SettingsUi @('invoke', $toggleId, '--action', "toggle-$($InitialState.ToLowerInvariant())") | Out-Null
        }
    } finally {
        $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflowId
    }
}
