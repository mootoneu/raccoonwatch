<#
.SYNOPSIS
Publishes raccoonwatch as a single-file Windows executable.
.EXAMPLE
./scripts/publish.ps1
.EXAMPLE
./scripts/publish.ps1 -SelfContained -Version 1.0.0
#>
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$SelfContained,
    [string]$Version = '1.1.0',
    [switch]$SkipZip
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\raccoonwatch\raccoonwatch.csproj'
$cliProject = Join-Path $repoRoot 'src\raccoonwatch.Cli\raccoonwatch.Cli.csproj'
$outputDirectory = Join-Path $repoRoot "artifacts\publish\$Runtime"
$executable = Join-Path $outputDirectory 'raccoonwatch.exe'
$icon = Join-Path $repoRoot 'assets\raccoonwatch.ico'
$artwork = Join-Path $repoRoot 'assets\raccoonwatch_icon_1254x1254.png'
$sourceIcon = Join-Path $repoRoot 'assets\raccoonwatch_icon_64x64.png'
$githubIcon = Join-Path $repoRoot 'assets\github-mark.png'

if (-not (Test-Path $project)) {
    throw "Project not found: $project"
}
foreach ($asset in @($icon, $artwork, $sourceIcon, $githubIcon)) {
    if (-not (Test-Path $asset)) {
        throw "Required asset not found: $asset. Check the assets folder and run scripts/generate-icon.ps1 if the ICO is missing."
    }
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$arguments = @(
    'publish', $project,
    '--configuration', $Configuration,
    '--runtime', $Runtime,
    "--self-contained", $SelfContained.IsPresent.ToString().ToLowerInvariant(),
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    "-p:Version=$Version",
    '--output', $outputDirectory
)

Write-Host "Publishing raccoonwatch $Version ($Runtime, SelfContained=$($SelfContained.IsPresent))..."
& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $executable)) {
    throw "Expected executable was not produced: $executable"
}

# Publish the companion CLI alongside the tray application.
$cliArguments = @(
    'publish', $cliProject,
    '--configuration', $Configuration,
    '--runtime', $Runtime,
    "--self-contained", $SelfContained.IsPresent.ToString().ToLowerInvariant(),
    '-p:PublishSingleFile=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    "-p:Version=$Version",
    '--output', $outputDirectory
)
Write-Host 'Publishing raccoonwatch-cli...'
& dotnet @cliArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish (CLI) failed with exit code $LASTEXITCODE"
}

$item = Get-Item $executable
Write-Host "Published: $($item.FullName) ($([math]::Round($item.Length / 1MB, 2)) MB)"

if (-not $SkipZip) {
    $zipPath = Join-Path $repoRoot "artifacts\raccoonwatch-$Version-$Runtime.zip"
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }
    Compress-Archive -Path $executable, (Join-Path $outputDirectory 'raccoonwatch-cli.exe') -DestinationPath $zipPath
    Write-Host "Archive: $zipPath"
}
