#Requires -Version 5.1

. (Join-Path $PSScriptRoot 'Publish-PathSafety.ps1')

function Confirm-WixEula {
    param([switch]$Accepted, [switch]$Interactive)

    if ($Accepted) { return $true }
    if (-not $Interactive) {
        throw 'Read https://docs.firegiant.com/wix/osmf/ and explicitly pass -AcceptWixEula, or use the interactive BAT launcher.'
    }
    Write-Host 'Building the MSI uses WiX Toolset 7.'
    Write-Host 'Read the EULA/OSMF terms: https://docs.firegiant.com/wix/osmf/'
    [string]$answer = Read-Host 'Do you accept these terms for this build? [y/N]'
    if ($answer.Trim() -notmatch '^(?i:y|yes)$') {
        throw 'WiX terms were not accepted. Publication cancelled.'
    }
    return $true
}

function Get-InstallerVersion {
    param([string]$ReleaseVersion, [string]$Override)

    if ($ReleaseVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$') {
        throw "Invalid release version: $ReleaseVersion"
    }
    # MSI accepts only the numeric core, not SemVer prerelease identifiers.
    [string]$version = ($ReleaseVersion -split '-', 2)[0]
    if ($ReleaseVersion -match '-' -and -not [string]::IsNullOrWhiteSpace($Override)) {
        $version = $Override
    }
    elseif ($Override -and $Override -ne $ReleaseVersion) {
        throw 'For a stable release, InstallerVersion must match the application version.'
    }
    if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        throw "Invalid MSI version: $version"
    }
    [version]$numericVersion = $version
    if ($numericVersion.Major -gt 255 -or $numericVersion.Minor -gt 255 -or $numericVersion.Build -gt 65535) {
        throw "MSI version exceeds 255.255.65535: $version"
    }
    return $version
}

function Get-InstallerId {
    param([string]$Prefix, [string]$Path)

    $hash = [System.Security.Cryptography.SHA256]::Create()
    try {
        [byte[]]$bytes = [Text.Encoding]::UTF8.GetBytes('SimpleGit11.Msi/' + $Path.Replace('\', '/').ToLowerInvariant())
        return $Prefix + ([BitConverter]::ToString($hash.ComputeHash($bytes))).Replace('-', '').Substring(0, 32)
    }
    finally { $hash.Dispose() }
}

function Assert-InstallerPayload {
    param([string]$CoreDirectory, [string]$SshDirectory, [string]$RepositoryRoot)

    $CoreDirectory = [IO.Path]::GetFullPath($CoreDirectory)
    $SshDirectory = [IO.Path]::GetFullPath($SshDirectory)
    Assert-NoReparsePointsInTree -Path $CoreDirectory -Root $RepositoryRoot | Out-Null
    Assert-NoReparsePointsInTree -Path $SshDirectory -Root $RepositoryRoot | Out-Null
    foreach ($file in @('SimpleGit11.exe', 'SimpleGit11.dll', 'SimpleGit11.pri', 'App.xbf', 'MainWindow.xbf', 'Assets\AppIcon.ico', 'LICENSE', 'Licenses\PACKAGES.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $CoreDirectory $file) -PathType Leaf)) {
            throw "Required core payload file is missing: $file"
        }
    }
    [string[]]$sshFiles = @('plugin.json', 'SimpleGit11.Plugin.Ssh.dll', 'SimpleGit11.Plugin.Ssh.deps.json', 'Renci.SshNet.dll', 'BouncyCastle.Cryptography.dll', 'Microsoft.Extensions.Logging.Abstractions.dll')
    foreach ($file in $sshFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $SshDirectory $file) -PathType Leaf)) {
            throw "Required SSH payload file is missing: $file"
        }
    }
    foreach ($file in Get-ChildItem -LiteralPath $CoreDirectory -Recurse -File -Force) {
        if ($file.Name -in @('SimpleGit11.Plugin.Ssh.dll', 'Renci.SshNet.dll', 'BouncyCastle.Cryptography.dll', 'plugin.json') -or
            $file.FullName.Substring($CoreDirectory.TrimEnd('\').Length).TrimStart('\') -like 'Plugins\*') {
            throw "SSH or plugin content leaked into the core payload: $($file.FullName)"
        }
    }
    foreach ($file in Get-ChildItem -LiteralPath $SshDirectory -File -Force) {
        if ($file.Name -notin ($sshFiles + @('LICENSE', 'THIRD-PARTY-NOTICES.txt'))) {
            throw "Unexpected SSH payload file (do not package the entire build output): $($file.Name)"
        }
    }
    foreach ($directory in Get-ChildItem -LiteralPath $SshDirectory -Directory -Force) {
        if ($directory.Name -ne 'Licenses') { throw "Unexpected SSH payload directory: $($directory.Name)" }
    }
    $manifest = Get-Content -LiteralPath (Join-Path $SshDirectory 'plugin.json') -Raw | ConvertFrom-Json
    if ($manifest.id -ne 'simplegit11.ssh' -or $manifest.apiVersion -ne '1.0' -or
        $manifest.entryAssembly -ne 'SimpleGit11.Plugin.Ssh.dll' -or $manifest.entryType -ne 'SimpleGit11.Plugin.Ssh.SshPlugin') {
        throw 'The SSH plugin manifest does not match the installer contract.'
    }
}

function Write-InstallerPayload {
    param([string]$CoreDirectory, [string]$SshDirectory, [string]$OutputPath, [string]$RepositoryRoot)

    Assert-InstallerPayload -CoreDirectory $CoreDirectory -SshDirectory $SshDirectory -RepositoryRoot $RepositoryRoot
    Assert-NoReparsePointUnderRoot -Path $OutputPath -Root $RepositoryRoot | Out-Null
    $document = New-Object System.Xml.XmlDocument
    [string]$namespace = 'http://wixtoolset.org/schemas/v4/wxs'
    $wix = $document.CreateElement('Wix', $namespace)
    [void]$document.AppendChild($wix)
    foreach ($payload in @(
        @{ Source = $CoreDirectory; Root = 'INSTALLFOLDER'; Group = 'CoreFiles' },
        @{ Source = $SshDirectory; Root = 'SshFolder'; Group = 'SshFiles' }
    )) {
        $fragment = $document.CreateElement('Fragment', $namespace)
        [void]$wix.AppendChild($fragment)
        $group = $document.CreateElement('ComponentGroup', $namespace)
        $group.SetAttribute('Id', $payload.Group)
        [void]$fragment.AppendChild($group)
        [string]$source = [IO.Path]::GetFullPath($payload.Source).TrimEnd('\')
        $directories = @{ '' = $payload.Root }
        foreach ($directory in Get-ChildItem -LiteralPath $source -Directory -Recurse -Force | Sort-Object FullName) {
            [string]$relative = $directory.FullName.Substring($source.Length + 1)
            [string]$parent = [IO.Path]::GetDirectoryName($relative)
            [string]$id = Get-InstallerId -Prefix 'D' -Path ($payload.Group + '/' + $relative)
            $directories[$relative] = $id
            $reference = $document.CreateElement('DirectoryRef', $namespace)
            $reference.SetAttribute('Id', $directories[$parent])
            $element = $document.CreateElement('Directory', $namespace)
            $element.SetAttribute('Id', $id)
            $element.SetAttribute('Name', $directory.Name)
            [void]$reference.AppendChild($element)
            [void]$fragment.AppendChild($reference)
        }
        foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse -Force | Sort-Object FullName) {
            [string]$relative = $file.FullName.Substring($source.Length + 1)
            [string]$id = Get-InstallerId -Prefix 'C' -Path ($payload.Group + '/' + $relative)
            $component = $document.CreateElement('Component', $namespace)
            $component.SetAttribute('Id', $id)
            # WiX cannot auto-generate GUIDs for mixed file/registry components.
            # Derive a stable identity from the feature and installation-relative path.
            $component.SetAttribute('Guid', [guid]::ParseExact($id.Substring(1), 'N').ToString('D'))
            $component.SetAttribute('Directory', $directories[[IO.Path]::GetDirectoryName($relative)])
            $element = $document.CreateElement('File', $namespace)
            $element.SetAttribute('Id', 'F' + $id.Substring(1))
            $element.SetAttribute('Source', $file.FullName)
            [void]$component.AppendChild($element)
            # HKMU follows the MSI context: HKCU for per-user, HKLM for per-machine.
            $registry = $document.CreateElement('RegistryValue', $namespace)
            $registry.SetAttribute('Root', 'HKMU')
            $registry.SetAttribute('Key', 'Software\SimpleGit11\Installer\Components')
            $registry.SetAttribute('Name', $id)
            $registry.SetAttribute('Type', 'integer')
            $registry.SetAttribute('Value', '1')
            $registry.SetAttribute('KeyPath', 'yes')
            [void]$component.AppendChild($registry)
            [void]$group.AppendChild($component)
        }
        foreach ($relative in @($directories.Keys | Sort-Object)) {
            if ($relative -eq '' -and $payload.Group -eq 'CoreFiles') { continue }
            [string]$id = Get-InstallerId -Prefix 'R' -Path ($payload.Group + '/' + $relative)
            $component = $document.CreateElement('Component', $namespace)
            $component.SetAttribute('Id', $id)
            $component.SetAttribute('Guid', '*')
            $component.SetAttribute('Directory', $directories[$relative])
            $registry = $document.CreateElement('RegistryValue', $namespace)
            $registry.SetAttribute('Root', 'HKMU')
            $registry.SetAttribute('Key', 'Software\SimpleGit11\Installer\Components')
            $registry.SetAttribute('Name', $id)
            $registry.SetAttribute('Type', 'integer')
            $registry.SetAttribute('Value', '1')
            $registry.SetAttribute('KeyPath', 'yes')
            [void]$component.AppendChild($registry)
            $remove = $document.CreateElement('RemoveFolder', $namespace)
            $remove.SetAttribute('Id', $id)
            $remove.SetAttribute('On', 'uninstall')
            [void]$component.AppendChild($remove)
            [void]$group.AppendChild($component)
        }
    }
    $document.Save($OutputPath)
}
