param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\Assets\AppIcon.ico')
)

$assetDirectory = Join-Path $PSScriptRoot '..\Assets'
$iconFrames = @(
    @{ Size = 16; Path = Join-Path $assetDirectory 'Square44x44Logo.targetsize-16.png' }
    @{ Size = 24; Path = Join-Path $assetDirectory 'Square44x44Logo.targetsize-24.png' }
    @{ Size = 32; Path = Join-Path $assetDirectory 'Square44x44Logo.targetsize-32.png' }
    @{ Size = 48; Path = Join-Path $assetDirectory 'Square44x44Logo.targetsize-48.png' }
    @{ Size = 256; Path = Join-Path $assetDirectory 'Square44x44Logo.targetsize-256.png' }
)

foreach ($frame in $iconFrames) {
    if (-not (Test-Path -LiteralPath $frame.Path)) {
        throw "Icon source does not exist: $($frame.Path)"
    }

    $frame.Bytes = [System.IO.File]::ReadAllBytes($frame.Path)
}

$stream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($stream)

try {
    $writer.Write([uint16]0) # Reserved
    $writer.Write([uint16]1) # ICO image
    $writer.Write([uint16]$iconFrames.Count)

    $imageOffset = 6 + (16 * $iconFrames.Count)
    foreach ($frame in $iconFrames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0) # Palette size
        $writer.Write([byte]0) # Reserved
        $writer.Write([uint16]1) # Color planes
        $writer.Write([uint16]32) # Bits per pixel
        $writer.Write([uint32]$frame.Bytes.Length)
        $writer.Write([uint32]$imageOffset)
        $imageOffset += $frame.Bytes.Length
    }

    foreach ($frame in $iconFrames) {
        $writer.Write($frame.Bytes)
    }

    $writer.Flush()
    [System.IO.File]::WriteAllBytes($OutputPath, $stream.ToArray())
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

Write-Output "Generated $OutputPath with frames: $($iconFrames.Size -join ', ') px"
