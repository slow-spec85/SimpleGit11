#Requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string]$CiReuseScript)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[string]$testSha = '1111111111111111111111111111111111111111'
[string]$testRepository = 'example/SimpleGit11'
[string]$originalToken = $env:GITHUB_TOKEN
[int]$scenarioCount = 0

function New-TestRun {
    param([long]$Number = 2, [long]$Id = 20)
    [pscustomobject]@{
        id = $Id; run_number = $Number; run_attempt = 1
        head_sha = $testSha; head_branch = 'main'; event = 'push'
        repository = [pscustomobject]@{ full_name = $testRepository }
        path = '.github/workflows/ci.yml'; status = 'completed'; conclusion = 'success'
    }
}

function Reset-Fixture {
    $env:GITHUB_TOKEN = 'test-only-token'
    $script:fixture = [pscustomobject]@{
        Runs = @(New-TestRun); RunCount = 1; Latest = New-TestRun
        Jobs = @([pscustomobject]@{
            name = 'Release x64'; run_id = 20; head_sha = $testSha
            status = 'completed'; conclusion = 'success'
            steps = @(
                [pscustomobject]@{ name = 'Build'; status = 'completed'; conclusion = 'success' },
                [pscustomobject]@{ name = 'Test application and SSH plugin'; status = 'completed'; conclusion = 'success' }
            )
        })
        JobCount = 1; FailAt = 0
        Calls = New-Object 'Collections.Generic.List[string]'
    }
}

# No network access: every API call is replaced, including timeout and permission failures.
function Invoke-RestMethod {
    param($Uri, $Headers, $Method, $TimeoutSec)
    if ($Method -ne 'Get' -or $TimeoutSec -ne 15 -or
        $Headers.Authorization -ne 'Bearer test-only-token') { throw 'Unexpected request options.' }
    $fixture.Calls.Add($Uri)
    if ($fixture.FailAt -eq $fixture.Calls.Count) { throw 'Simulated 403/429/timeout with test-only-token.' }
    switch ($fixture.Calls.Count) {
        1 {
            [string]$expected = "https://api.github.com/repos/$testRepository/actions/workflows/ci.yml/runs?branch=main&event=push&head_sha=$testSha&per_page=100"
            if ($Uri -ne $expected) { throw 'CI lookup must use the workflow, main, push and exact SHA without a success filter.' }
            return [pscustomobject]@{ total_count = $fixture.RunCount; workflow_runs = $fixture.Runs }
        }
        2 {
            if ($Uri -ne "https://api.github.com/repos/$testRepository/actions/runs/20") { throw 'The newest run was not selected.' }
            return $fixture.Latest
        }
        3 {
            [string]$expected = "https://api.github.com/repos/$testRepository/actions/runs/20/attempts/$($fixture.Latest.run_attempt)/jobs?per_page=100"
            if ($Uri -ne $expected) { throw 'Jobs must belong to the latest explicit attempt.' }
            return [pscustomobject]@{ total_count = $fixture.JobCount; jobs = $fixture.Jobs }
        }
        default { throw 'Unexpected extra API request.' }
    }
}

function Assert-Decision {
    param([string]$Name, [bool]$Expected, [int]$Requests)
    $decision = & $CiReuseScript -Repository $testRepository -CommitSha $testSha
    if ($decision.Reuse -ne $Expected -or $fixture.Calls.Count -ne $Requests) {
        throw "Scenario '$Name' failed: reuse=$($decision.Reuse), requests=$($fixture.Calls.Count), reason=$($decision.Reason)"
    }
    if ([string]::IsNullOrWhiteSpace($decision.Reason) -or $decision.Reason -match 'test-only-token') {
        throw 'Every decision must explain the result without disclosing credentials.'
    }
    if ($Expected -and $decision.RunUrl -ne "https://github.com/$testRepository/actions/runs/20/attempts/$($fixture.Latest.run_attempt)") {
        throw 'Successful reuse must link to the verified run attempt.'
    }
    $script:scenarioCount++
}

try {
    Reset-Fixture
    Assert-Decision 'success' $true 3
    Reset-Fixture
    $fixture.Latest.run_attempt = 2
    Assert-Decision 'successful rerun' $true 3
    Reset-Fixture
    $fixture.Runs = @((New-TestRun -Number 1 -Id 99), (New-TestRun))
    $fixture.RunCount = 2
    Assert-Decision 'select by run number, not response order or ID' $true 3
    Reset-Fixture
    $fixture.Runs[0].path = '.github/workflows/ci.yml@refs/heads/main'
    $fixture.Latest.path = '.github/workflows/ci.yml@refs/heads/main'
    Assert-Decision 'qualified workflow path' $true 3

    foreach ($field in @('head_sha', 'head_branch', 'event', 'path', 'repository')) {
        Reset-Fixture
        switch ($field) {
            head_sha { $fixture.Runs[0].head_sha = '2222222222222222222222222222222222222222' }
            head_branch { $fixture.Runs[0].head_branch = 'dev' }
            event { $fixture.Runs[0].event = 'pull_request' }
            path { $fixture.Runs[0].path = '.github/workflows/other.yml' }
            repository { $fixture.Runs[0].repository.full_name = 'another/SimpleGit11' }
        }
        Assert-Decision "reject unrelated $field" $false 1
    }
    foreach ($conclusion in @('failure', 'cancelled', 'timed_out', 'skipped', 'neutral')) {
        Reset-Fixture
        $fixture.Runs[0].conclusion = $conclusion
        $fixture.Runs += New-TestRun -Number 1 -Id 10
        $fixture.RunCount = 2
        Assert-Decision "latest $conclusion overrides old success" $false 1
    }
    foreach ($status in @('queued', 'in_progress', 'waiting')) {
        Reset-Fixture
        $fixture.Runs[0].status = $status
        $fixture.Runs[0].conclusion = $null
        Assert-Decision "latest $status" $false 1
    }
    Reset-Fixture
    $fixture.Latest.status = 'in_progress'
    Assert-Decision 'rerun starts during lookup' $false 2
    Reset-Fixture
    $fixture.Latest.head_sha = '2222222222222222222222222222222222222222'
    Assert-Decision 'refreshed identity mismatch' $false 2
    Reset-Fixture
    $fixture.Runs = @(); $fixture.RunCount = 0
    Assert-Decision 'no previous CI' $false 1
    Reset-Fixture
    $fixture.RunCount = 101
    Assert-Decision 'incomplete run history' $false 1
    Reset-Fixture
    $fixture.JobCount = 101
    Assert-Decision 'incomplete job history' $false 3
    Reset-Fixture
    $fixture.Jobs = @(); $fixture.JobCount = 0
    Assert-Decision 'missing build job' $false 3
    Reset-Fixture
    $fixture.Jobs[0].conclusion = 'skipped'
    Assert-Decision 'skipped build job' $false 3
    Reset-Fixture
    $fixture.Jobs[0].run_id = 10
    Assert-Decision 'job from another run' $false 3
    foreach ($stepIndex in @(0, 1)) {
        Reset-Fixture
        $fixture.Jobs[0].steps[$stepIndex].conclusion = 'skipped'
        Assert-Decision "required step $stepIndex skipped" $false 3
    }
    Reset-Fixture
    $fixture.Jobs[0].steps[1].name = 'Test'
    Assert-Decision 'old CI did not run SSH plugin tests' $false 3
    foreach ($request in @(1, 2, 3)) {
        Reset-Fixture
        $fixture.FailAt = $request
        Assert-Decision "API failure at request $request" $false $request
    }
    Reset-Fixture
    $fixture.Latest = [pscustomobject]@{ unexpected = 'data' }
    Assert-Decision 'malformed response' $false 2
    Reset-Fixture
    $env:GITHUB_TOKEN = ''
    Assert-Decision 'no token' $false 0

    # Verify the fail-safe wiring, without evaluating untrusted workflow commands.
    [string]$root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $CiReuseScript))
    [string]$workflow = Get-Content -LiteralPath (Join-Path $root '.github/workflows/release-validation.yml') -Raw
    foreach ($required in @(
        'actions: read', 'needs: validate-release-tag',
        'reuse-ci: ${{ steps.ci.outputs.reuse }}',
        "if: needs.validate-release-tag.outputs.reuse-ci != 'true'",
        'continue-on-error: true', '"reuse=false" >> $env:GITHUB_OUTPUT',
        'git rev-parse HEAD', 'git merge-base --is-ancestor HEAD refs/remotes/origin/main',
        '-Repository $env:GITHUB_REPOSITORY -CommitSha $env:RELEASE_COMMIT_SHA'
    )) {
        if (-not $workflow.Contains($required)) { throw "Missing release workflow guard: $required" }
    }
    $runBlocks = [regex]::Matches($workflow, '(?m)^        run: \|\r?\n(?<Script>(?:^          .*\r?\n|^\r?\n)+)')
    if ($runBlocks.Count -ne 2) { throw 'Expected tag-validation and CI-lookup PowerShell blocks.' }
    foreach ($block in $runBlocks) {
        [string]$scriptText = $block.Groups['Script'].Value -replace '(?m)^          ', ''
        $tokens = $null
        $parseErrors = $null
        [void][Management.Automation.Language.Parser]::ParseInput($scriptText, [ref]$tokens, [ref]$parseErrors)
        if ($parseErrors.Count -ne 0) { throw 'A release workflow PowerShell block has syntax errors.' }
    }
    Write-Host "Release CI reuse: $scenarioCount scenarios and workflow guards passed without network access."
}
finally { $env:GITHUB_TOKEN = $originalToken }
