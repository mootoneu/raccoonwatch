<#
.SYNOPSIS
Converts an SVG file to a PNG using ImageMagick.

.EXAMPLE
.\scripts\convert-svg-to-png.ps1 .\assets\logo.svg

.EXAMPLE
.\scripts\convert-svg-to-png.ps1 .\assets\logo.svg -Output .\assets\logo-512.png -Width 512

.EXAMPLE
.\scripts\convert-svg-to-png.ps1 .\assets\logo.svg -Magick 'C:\Program Files\ImageMagick-7.1.2-Q16-HDRI\magick.exe'
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Source,

    [string]$Output,

    [int]$Width,

    [int]$Height,

    [int]$Density = 300,

    [string]$Magick
)

$ErrorActionPreference = 'Stop'

function Resolve-MagickExecutable([string]$ExplicitPath) {
    if ($ExplicitPath) {
        if (-not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
            throw "ImageMagick executable not found at '$ExplicitPath'."
        }
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $command = Get-Command magick.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    # ImageMagick is not always added to PATH immediately after installation.
    $roots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { $_ }
    $installed = foreach ($root in $roots) {
        Get-ChildItem -Path $root -Directory -Filter 'ImageMagick-*' -ErrorAction SilentlyContinue |
            ForEach-Object { Get-ChildItem -Path $_.FullName -Filter 'magick.exe' -ErrorAction SilentlyContinue }
    }
    $installed = $installed | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $installed) {
        throw 'ImageMagick not found. Install it or pass -Magick with the path to magick.exe.'
    }
    return $installed.FullName
}

try {
    $magickExe = Resolve-MagickExecutable $Magick
    $sourcePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Source)
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Source file not found: $sourcePath"
    }

    if (-not $Output) {
        $Output = [IO.Path]::ChangeExtension($sourcePath, '.png')
    }
    $outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)

    $arguments = @('-background', 'none', '-density', "$Density", $sourcePath)
    if ($Width -or $Height) {
        $size = "$(if ($Width) { $Width } else { '' })x$(if ($Height) { $Height } else { '' })"
        $arguments += @('-resize', $size)
    }
    $arguments += $outputPath

    $outputDirectory = Split-Path -Parent $outputPath
    if ($outputDirectory -and -not (Test-Path -LiteralPath $outputDirectory)) {
        New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    }

    & $magickExe @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "ImageMagick failed with exit code $LASTEXITCODE."
    }

    Write-Host "Wrote $outputPath"
}
catch {
    Write-Error $_
    exit 1
}
