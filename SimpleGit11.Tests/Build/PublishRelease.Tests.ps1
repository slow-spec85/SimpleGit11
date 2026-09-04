#Requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishScript)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PublishScript) 'Publish-PathSafety.ps1')
[string]$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('SimpleGit11.Publish.' + [guid]::NewGuid().ToString('N'))
[string]$buildDirectory = Join-Path $testRoot 'SimpleGit11\Build'
$global:publicationTest = [pscustomobject]@{ Tag = 'v1.2.3'; PublishCalls = 0; FailInstaller = $false; FixtureVersion = '1.2.3'; Consent = ''; Prompts = 0 }

function Read-Host {
    param($Prompt)
    $global:publicationTest.Prompts++
    $global:publicationTest.Consent
}

# All external operations are replaced. No real Git, SDK or application process is used.
function git {
    $global:LASTEXITCODE = 0
    if ('tag' -in $args) { $global:publicationTest.Tag }
}
function Get-Process { param($Name, $ErrorAction) }
function Get-Item {
    param($LiteralPath)
    if ($LiteralPath -like '*\SimpleGit11.exe') {
        return [pscustomobject]@{ VersionInfo = [pscustomobject]@{ ProductVersion = $global:publicationTest.FixtureVersion } }
    }
    Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath
}
function dotnet {
    $global:publicationTest.PublishCalls++
    $global:LASTEXITCODE = 0
    'Fixture dotnet publish output'
    [string]$output = @($args | Where-Object { $_ -like '-p:PublishDir=*' })[0].Substring('-p:PublishDir='.Length)
    $overrides = @($args | Where-Object { $_ -like '-p:MinVerVersionOverride=*' })
    $global:publicationTest.FixtureVersion = if ($overrides.Count) { $overrides[0].Substring('-p:MinVerVersionOverride='.Length) } else { $global:publicationTest.Tag.Substring(1) }
    foreach ($relative in @('SimpleGit11.exe', 'SimpleGit11.dll', 'SimpleGit11.pri', 'App.xbf', 'MainWindow.xbf',
        'Controls\DiffViewer.xbf', 'Dialogs\AboutDialog.xbf', 'Dialogs\CommitDialog.xbf', 'Pages\SettingsPage.xbf',
        'Assets\AppIcon.ico', 'LICENSE', 'THIRD-PARTY-NOTICES.txt', 'Licenses\PACKAGES.txt')) {
        [string]$path = Join-Path $output $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        Set-Content -LiteralPath $path -Value 'fixture'
    }
}
function Assert-Rejected {
    param([scriptblock]$Action, [string]$Message)
    try { & $Action | Out-Null }
    catch { if ($_.Exception.Message -notlike "*$Message*") { throw }; return }
    throw "Expected rejection: $Message"
}
try {
    New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
    foreach ($name in @('Publish-Release.ps1', 'Publish-PathSafety.ps1', 'Installer-Payload.ps1')) {
        Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $PublishScript) $name) -Destination $buildDirectory
    }
    Set-Content -LiteralPath (Join-Path $testRoot 'SimpleGit11\SimpleGit11.csproj') -Value '<Project />'
    Set-Content -LiteralPath (Join-Path $buildDirectory 'SourceComponents.json') -Value '[]'
    Set-Content -LiteralPath (Join-Path $buildDirectory 'Collect-ReleaseLicenses.ps1') -Value 'param($RepositoryRoot, $ProjectAssetsPath, $SourceComponentsPath, $PublishedDirectory)'
    Set-Content -LiteralPath (Join-Path $buildDirectory 'Build-Installer.ps1') -Value @'
param($CoreDirectory, $ReleaseVersion, $InstallerVersion, [switch]$AcceptWixEula)
if (-not $AcceptWixEula) { throw 'Consent was not forwarded.' }
if ($global:publicationTest.FailInstaller) { throw 'Fixture installer failure.' }
if ((Split-Path -Leaf $CoreDirectory) -ne '.publish-staging-win-x64') { throw 'Payload must remain internal.' }
foreach ($culture in @('en-US', 'ru-RU')) {
    $path = Join-Path (Split-Path -Parent $CoreDirectory) "SimpleGit11-$ReleaseVersion-win-x64-$culture.msi"
    Set-Content -LiteralPath $path -Value $InstallerVersion
    Set-Content -LiteralPath "$path.sha256" -Value 'fixture checksum'
    $path
}
'@
    [string]$fixtureScript = Join-Path $buildDirectory 'Publish-Release.ps1'
    if ((Get-Command $fixtureScript).Parameters.ContainsKey('Installer')) { throw 'The optional installer mode must be removed.' }
    Assert-Rejected { & $fixtureScript } 'AcceptWixEula'
    if ($global:publicationTest.Prompts -ne 0) { throw 'Noninteractive execution must never prompt.' }
    foreach ($answer in @('', 'n', 'maybe')) {
        $global:publicationTest.Consent = $answer
        Assert-Rejected { & $fixtureScript -Interactive } 'not accepted'
    }
    Assert-Rejected { & $fixtureScript -InstallerVersion 1.2.4 -AcceptWixEula } 'must match'
    if ($global:publicationTest.PublishCalls -ne 0) { throw 'Invalid options must fail before publishing.' }

    $result = & $fixtureScript -AcceptWixEula
    if ($result.Version -ne '1.2.3' -or $result.InstallerVersion -ne '1.2.3' -or
        $result.Installers.Count -ne 2 -or $result.ChecksumFiles.Count -ne 2) { throw 'Invalid stable MSI result.' }
    if ('Archive' -in $result.PSObject.Properties.Name -or 'Sha256' -in $result.PSObject.Properties.Name) { throw 'Obsolete archive output remains.' }
    foreach ($file in @($result.Installers) + @($result.ChecksumFiles)) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing output: $file" }
    }
    $global:publicationTest.Consent = ' y '
    $result = & $fixtureScript -Interactive
    if ($result.InstallerVersion -ne '1.2.3') { throw 'Interactive release failed without version arguments.' }
    $global:publicationTest.Tag = 'v1.2.4-preview.1'
    $result = & $fixtureScript -Interactive
    if ($result.InstallerVersion -ne '1.2.4') { throw 'Prerelease version must be automatic.' }
    $result = & $fixtureScript -InstallerVersion 1.2.4 -AcceptWixEula
    if ($result.Version -ne '1.2.4-preview.1' -or $result.InstallerVersion -ne '1.2.4') { throw 'Prerelease versions were not preserved.' }
    $result = & $fixtureScript -DevelopmentBuild -Interactive
    if ($result.InstallerVersion -ne ($result.Version -split '-', 2)[0]) { throw 'Development version must come from the application.' }
    [int]$promptsBefore = $global:publicationTest.Prompts
    $result = & $fixtureScript -DevelopmentBuild -Interactive -AcceptWixEula
    if ($global:publicationTest.Prompts -ne $promptsBefore) { throw 'Explicit consent must skip the prompt.' }
    $result = & $fixtureScript -DevelopmentBuild -InstallerVersion 1.2.5 -AcceptWixEula
    if ($result.Version -notmatch '-dev.local.\d+$' -or $result.InstallerVersion -ne '1.2.5') { throw 'Development MSI result is incorrect.' }
    $global:publicationTest.FailInstaller = $true
    Assert-Rejected { & $fixtureScript -DevelopmentBuild -InstallerVersion 1.2.6 -AcceptWixEula } 'Fixture installer failure'
    if (@(Get-ChildItem -LiteralPath (Join-Path $testRoot 'artifacts') -File -Recurse -Filter '*.zip*').Count) { throw 'Publishing must not create archives.' }
    if (@(Get-ChildItem -LiteralPath (Join-Path $testRoot 'artifacts') -Directory | Where-Object Name -NotLike '.*').Count) {
        throw 'Publishing must not create a standalone application distribution.'
    }
    [string]$repositoryRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PublishScript))
    foreach ($name in @('Publish-Release.cmd.bat', 'Publish-Release-dev.cmd.bat')) {
        [string]$batch = Get-Content -LiteralPath (Join-Path $repositoryRoot $name) -Raw
        if ($batch -notmatch '-Interactive' -or $batch -notmatch '%\*' -or $batch -match '-AcceptWixEula') {
            throw 'BAT must select interactive consent and preserve optional arguments.'
        }
    }
    Write-Host 'MSI-only publishing, validation, failure propagation and result contract passed.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-DirectoryUnderRoot $testRoot ([IO.Path]::GetTempPath()) }
    Remove-Variable -Name publicationTest -Scope Global
}
