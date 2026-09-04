#Requires -Version 5.1

function Get-FullPathUnderRoot {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Root
    )

    [char[]]$directorySeparators = @(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    )
    [string]$fullPath = [System.IO.Path]::GetFullPath($Path)
    [string]$fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd($directorySeparators)
    [string]$rootPrefix = $fullRoot + [System.IO.Path]::DirectorySeparatorChar
    [bool]$isRoot = $fullPath.Equals(
        $fullRoot,
        [System.StringComparison]::OrdinalIgnoreCase)

    if (-not $isRoot -and
        -not $fullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the repository root: $fullPath"
    }

    return $fullPath
}

function Assert-NoReparsePointUnderRoot {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Root
    )

    [char[]]$directorySeparators = @(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    )
    [string]$fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd($directorySeparators)
    [string]$fullPath = Get-FullPathUnderRoot -Path $Path -Root $fullRoot
    [string]$relativePath = $fullPath.Substring($fullRoot.Length).TrimStart($directorySeparators)

    if ([string]::IsNullOrEmpty($relativePath)) {
        return $fullPath
    }

    [string]$currentPath = $fullRoot
    [string[]]$pathComponents = $relativePath.Split(
        $directorySeparators,
        [System.StringSplitOptions]::RemoveEmptyEntries)

    foreach ($pathComponent in $pathComponents) {
        $item = Get-ChildItem -LiteralPath $currentPath -Force -ErrorAction Stop |
            Where-Object {
                $_.Name.Equals(
                    $pathComponent,
                    [System.StringComparison]::OrdinalIgnoreCase)
            } |
            Select-Object -First 1
        if ($null -eq $item) {
            break
        }

        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Path contains a reparse point and cannot be used for publication: $($item.FullName)"
        }

        $currentPath = $item.FullName
    }

    return $fullPath
}

function Assert-NoReparsePointsInTree {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Root
    )

    [string]$safePath = Assert-NoReparsePointUnderRoot -Path $Path -Root $Root
    if (-not (Test-Path -LiteralPath $safePath -PathType Container)) {
        throw "Directory was not found: $safePath"
    }

    $pendingDirectories = New-Object 'System.Collections.Generic.Stack[string]'
    $pendingDirectories.Push($safePath)

    while ($pendingDirectories.Count -gt 0) {
        [string]$currentDirectory = $pendingDirectories.Pop()
        foreach ($item in Get-ChildItem `
            -LiteralPath $currentDirectory `
            -Force `
            -ErrorAction Stop) {
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Publication tree contains a reparse point: $($item.FullName)"
            }

            if ($item.PSIsContainer) {
                $pendingDirectories.Push($item.FullName)
            }
        }
    }

    return $safePath
}

function Remove-DirectoryUnderRoot {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Root
    )

    [string]$safePath = Assert-NoReparsePointUnderRoot -Path $Path -Root $Root
    [string]$fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    if ($safePath.Equals($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove the repository root: $safePath"
    }

    if (Test-Path -LiteralPath $safePath) {
        if (-not (Test-Path -LiteralPath $safePath -PathType Container)) {
            throw "Expected a directory: $safePath"
        }

        Assert-NoReparsePointsInTree -Path $safePath -Root $Root | Out-Null
        Remove-Item -LiteralPath $safePath -Recurse -Force
    }
}

function Remove-FileUnderRoot {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Root
    )

    [string]$safePath = Assert-NoReparsePointUnderRoot -Path $Path -Root $Root
    [string]$fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    if ($safePath.Equals($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove the repository root: $safePath"
    }

    if (Test-Path -LiteralPath $safePath) {
        if (Test-Path -LiteralPath $safePath -PathType Container) {
            throw "Expected a file: $safePath"
        }

        Remove-Item -LiteralPath $safePath -Force
    }
}
