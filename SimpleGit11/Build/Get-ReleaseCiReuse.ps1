#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$CommitSha
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-CiDecision {
    param([bool]$Reuse, [string]$Reason, [string]$RunUrl = '')
    [pscustomobject]@{ Reuse = $Reuse; Reason = $Reason; RunUrl = $RunUrl }
}

function Test-RunContext {
    param($Run)
    return $Run.head_sha -ieq $CommitSha -and $Run.head_branch -ceq 'main' -and
        $Run.event -ceq 'push' -and $Run.repository.full_name -ieq $Repository -and
        ($Run.path -split '@', 2)[0] -ceq '.github/workflows/ci.yml'
}

if ([string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
    return New-CiDecision $false 'No GitHub token is available to confirm previous CI.'
}

# No external modules or actions are required. Only read-only GitHub API calls are used.
$headers = @{
    Accept = 'application/vnd.github+json'
    Authorization = "Bearer $env:GITHUB_TOKEN"
    'User-Agent' = 'SimpleGit11-ReleaseValidation'
    'X-GitHub-Api-Version' = '2026-03-10'
}
[string]$apiRoot = "https://api.github.com/repos/$Repository/actions"
function Read-GitHubApi {
    param([string]$RelativePath)
    Invoke-RestMethod -Uri "$apiRoot/$RelativePath" -Headers $headers -Method Get -TimeoutSec 15
}

try {
    # Do not filter by success: a newer failed, cancelled or running attempt must
    # not be hidden behind an older successful run for the same commit.
    $response = Read-GitHubApi "workflows/ci.yml/runs?branch=main&event=push&head_sha=$CommitSha&per_page=100"
    $runs = @($response.workflow_runs)
    if ([int]$response.total_count -ne $runs.Count) {
        return New-CiDecision $false 'CI history is incomplete; previous success cannot be confirmed.'
    }
    $matchingRuns = @($runs | Where-Object { Test-RunContext $_ } |
        Sort-Object @{ Expression = { [long]$_.run_number }; Descending = $true })
    if ($matchingRuns.Count -eq 0) {
        return New-CiDecision $false 'No CI push run on main matches the exact release commit.'
    }
    $latest = $matchingRuns[0]
    if ($latest.status -cne 'completed' -or $latest.conclusion -cne 'success') {
        return New-CiDecision $false 'The latest matching CI run has not completed successfully.'
    }
    [long]$runId = $latest.id
    if ($runId -le 0) { throw 'Invalid CI run identifier.' }

    # Refresh the selected run before checking its explicit attempt, so a rerun
    # started after the history lookup is not mistaken for a completed success.
    $run = Read-GitHubApi "runs/$runId"
    if (-not (Test-RunContext $run) -or [long]$run.id -ne $runId -or
        [long]$run.run_number -ne [long]$latest.run_number -or
        $run.status -cne 'completed' -or $run.conclusion -cne 'success') {
        return New-CiDecision $false 'The selected CI run changed or is no longer successful.'
    }
    [int]$attempt = $run.run_attempt
    if ($attempt -le 0) { throw 'Invalid CI attempt identifier.' }
    $jobResponse = Read-GitHubApi "runs/$runId/attempts/$attempt/jobs?per_page=100"
    $jobs = @($jobResponse.jobs)
    if ([int]$jobResponse.total_count -ne $jobs.Count) {
        return New-CiDecision $false 'CI job history is incomplete.'
    }
    $buildJobs = @($jobs | Where-Object { $_.name -ceq 'Release x64' })
    if ($buildJobs.Count -ne 1 -or $buildJobs[0].status -cne 'completed' -or
        $buildJobs[0].conclusion -cne 'success' -or [long]$buildJobs[0].run_id -ne $runId -or
        $buildJobs[0].head_sha -ine $CommitSha) {
        return New-CiDecision $false 'A successful Release x64 job could not be confirmed.'
    }
    foreach ($stepName in @('Build', 'Test application and SSH plugin')) {
        $steps = @($buildJobs[0].steps | Where-Object { $_.name -ceq $stepName })
        if ($steps.Count -ne 1 -or $steps[0].status -cne 'completed' -or $steps[0].conclusion -cne 'success') {
            return New-CiDecision $false 'The required build and solution-wide test steps did not both succeed.'
        }
    }
    return New-CiDecision $true 'Build and tests already passed for this exact release commit on main.' `
        "https://github.com/$Repository/actions/runs/$runId/attempts/$attempt"
}
catch {
    # Do not log exception details or request headers: they may contain credentials.
    return New-CiDecision $false 'GitHub API lookup failed or returned unexpected data; CI will run normally.'
}
