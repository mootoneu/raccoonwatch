<#
.SYNOPSIS
Generates assets/raccoonwatch.ico from the supplied PNG (16/24/32/48/64 px).
Each entry is a 32-bpp BMP/DIB image (not a PNG-compressed entry), which is what
the Windows notification area and WinForms title bars render reliably.
#>
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\assets\raccoonwatch_icon_64x64.png'),
    [string]$Output = (Join-Path $PSScriptRoot '..\assets\raccoonwatch.ico')
)

Add-Type -AssemblyName System.Drawing

$sourceImage = [System.Drawing.Image]::FromFile((Resolve-Path $Source).Path)

function New-DibImage([int]$Size, [System.Drawing.Image]$SourceImage) {
    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($SourceImage, 0, 0, $Size, $Size)
        $graphics.Dispose()

        # 32-bpp BGRA pixels, bottom-up rows (DIB convention).
        $xor = New-Object byte[] ($Size * $Size * 4)
        $position = 0
        for ($y = $Size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $Size; $x++) {
                $color = $bitmap.GetPixel($x, $y)
                $xor[$position++] = $color.B
                $xor[$position++] = $color.G
                $xor[$position++] = $color.R
                $xor[$position++] = $color.A
            }
        }

        # AND mask (1 bpp, rows DWORD-aligned). Zeros let the BGRA alpha channel
        # control transparency.
        $maskRowBytes = [int][math]::Ceiling($Size / 32.0) * 4
        $andMask = New-Object byte[] ($maskRowBytes * $Size)

        $stream = New-Object System.IO.MemoryStream
        $writer = New-Object System.IO.BinaryWriter($stream)
        $writer.Write([uint32]40)                    # BITMAPINFOHEADER size
        $writer.Write([int32]$Size)                   # width
        $writer.Write([int32]($Size * 2))             # height: XOR + AND
        $writer.Write([uint16]1)                      # planes
        $writer.Write([uint16]32)                     # bits per pixel
        $writer.Write([uint32]0)                      # BI_RGB
        $writer.Write([uint32]($Size * $Size * 4))    # XOR size
        $writer.Write([int32]0)                       # pixels per meter X
        $writer.Write([int32]0)                       # pixels per meter Y
        $writer.Write([uint32]0)                      # colors used
        $writer.Write([uint32]0)                      # important colors
        $writer.Write($xor)
        $writer.Write($andMask)
        $writer.Flush()
        return ,$stream.ToArray()
    }
    finally {
        $bitmap.Dispose()
    }
}

try {
    $sizes = @(16, 24, 32, 48, 64)
    $images = @()
    foreach ($size in $sizes) {
        $images += ,(New-DibImage -Size $size -SourceImage $sourceImage)
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
