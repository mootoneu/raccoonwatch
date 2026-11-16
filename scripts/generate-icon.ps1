<#
.SYNOPSIS
Generates assets/raccoonwatch.ico from the supplied PNG (16/24/32/48/64 px).
PNG-compressed ICO entries are supported on Windows Vista and later, which covers
the Windows 10/11 target of raccoonwatch.
#>
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\raccoonwatch_icon_64x64.png'),
    [string]$Output = (Join-Path $PSScriptRoot '..\assets\raccoonwatch.ico')
)

Add-Type -AssemblyName System.Drawing

$sourceImage = [System.Drawing.Image]::FromFile((Resolve-Path $Source).Path)
try {
    $sizes = @(16, 24, 32, 48, 64)
    $images = @()
    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($sourceImage, 0, 0, $size, $size)
        $graphics.Dispose()

        $stream = New-Object System.IO.MemoryStream
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
        $images += ,$stream.ToArray()
    }

    $offset = 6 + 16 * $images.Count
    $entries = @()
    for ($i = 0; $i -lt $images.Count; $i++) {
        $bytes = $images[$i]
        $entries += [pscustomobject]@{
            Size   = $sizes[$i]
            Offset = $offset
            Bytes  = $bytes
        }
        $offset += $bytes.Length
    }

    $outputDirectory = Split-Path -Parent $Output
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

    $fileStream = [System.IO.File]::Create($Output)
    try {
        $writer = New-Object System.IO.BinaryWriter($fileStream)
        $writer.Write([uint16]0)               # reserved
        $writer.Write([uint16]1)               # type: icon
        $writer.Write([uint16]$images.Count)   # image count

        foreach ($entry in $entries) {
            $dimension = [byte]$(if ($entry.Size -ge 256) { 0 } else { $entry.Size })
            $writer.Write($dimension)          # width
            $writer.Write($dimension)          # height
            $writer.Write([byte]0)             # palette size
            $writer.Write([byte]0)             # reserved
            $writer.Write([uint16]1)           # color planes
            $writer.Write([uint16]32)          # bits per pixel
            $writer.Write([uint32]$entry.Bytes.Length)
            $writer.Write([uint32]$entry.Offset)
        }

        foreach ($entry in $entries) {
            $writer.Write($entry.Bytes)
        }
        $writer.Flush()
    }
    finally {
        $fileStream.Dispose()
    }

    Write-Host "Generated $Output"
}
finally {
    $sourceImage.Dispose()
}
