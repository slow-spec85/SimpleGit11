#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PathSafetyScript
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. $PathSafetyScript

function Assert-ReparsePointRejected {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Action
    )

    $caughtError = $null
    try {
        & $Action
    }
    catch {
        $caughtError = $_
    }

    if ($null -eq $caughtError) {
        throw "Expected the reparse point to be rejected."
    }

    if ($caughtError.Exception.Message -notmatch 'reparse point') {
        throw "Unexpected error: $($caughtError.Exception.Message)"
    }
}

[string]$testBase = Join-Path `
    ([System.IO.Path]::GetTempPath()) `
    ("SimpleGit11.PublishPathSafety." + [Guid]::NewGuid().ToString("N"))
[string]$repositoryRoot = Join-Path $testBase "repository"
[string]$outsideDirectory = Join-Path $testBase "outside"
[string]$outsideMarker = Join-Path $outsideDirectory "do-not-delete.txt"
[string]$artifactLink = Join-Path $repositoryRoot "artifacts"
[string]$stagingDirectory = Join-Path $repositoryRoot "staging"
[string]$nestedLink = Join-Path $stagingDirectory "linked-content"

try {
    New-Item -ItemType Directory -Path $repositoryRoot | Out-Null
    New-Item -ItemType Directory -Path $outsideDirectory | Out-Null
    Set-Content -LiteralPath $outsideMarker -Value "outside" -Encoding Ascii

    New-Item -ItemType Junction -Path $artifactLink -Target $outsideDirectory | Out-Null

    Assert-ReparsePointRejected -Action {
        Remove-DirectoryUnderRoot -Path $artifactLink -Root $repositoryRoot
    }
    Assert-ReparsePointRejected -Action {
        Assert-NoReparsePointUnderRoot `
            -Path (Join-Path $artifactLink "do-not-delete.txt") `
            -Root $repositoryRoot | Out-Null
    }

    if (-not (Test-Path -LiteralPath $outsideMarker -PathType Leaf)) {
        throw "The external marker was deleted through the junction."
    }

    [System.IO.Directory]::Delete($artifactLink)
    New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
    New-Item -ItemType Junction -Path $nestedLink -Target $outsideDirectory | Out-Null

    Assert-ReparsePointRejected -Action {
        Assert-NoReparsePointsInTree -Path $stagingDirectory -Root $repositoryRoot | Out-Null
    }

    if (-not (Test-Path -LiteralPath $outsideMarker -PathType Leaf)) {
        throw "The external marker was deleted while validating the publication tree."
    }
}
finally {
    if (Test-Path -LiteralPath $artifactLink) {
        [System.IO.Directory]::Delete($artifactLink)
    }

    if (Test-Path -LiteralPath $nestedLink) {
        [System.IO.Directory]::Delete($nestedLink)
    }

    if (Test-Path -LiteralPath $testBase) {
        Remove-Item -LiteralPath $testBase -Recurse -Force
    }
}
