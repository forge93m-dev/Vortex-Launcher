param([string]$OutputDirectory = 'publish\win-x64')
$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "Vortex Launcher.csproj"
$output = Join-Path $PSScriptRoot $OutputDirectory

# Do not remove this directory: portable users may store their instances and
# settings beside the executable. Publish replaces application files only.

dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $output `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) { throw "Publication failed (exit code $LASTEXITCODE)." }

Write-Host "Published to: $output" -ForegroundColor Green
[xml]$projectXml = Get-Content -LiteralPath $project
$appVersion = [string]$projectXml.Project.PropertyGroup.Version
$releaseFolder = Join-Path $PSScriptRoot 'release'
New-Item -ItemType Directory -Path $releaseFolder -Force | Out-Null
$archivePath = Join-Path $releaseFolder "VortexLauncher-$appVersion-win-x64.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archiveStream = [IO.File]::Open($archivePath, [IO.FileMode]::Create)
try {
    $archive = [IO.Compression.ZipArchive]::new($archiveStream, [IO.Compression.ZipArchiveMode]::Create)
    try { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $output 'Vortex Launcher.exe'), 'Vortex Launcher.exe', [IO.Compression.CompressionLevel]::Optimal) | Out-Null }
    finally { $archive.Dispose() }
} finally { $archiveStream.Dispose() }
Write-Host "Updater package: $archivePath" -ForegroundColor Green
Write-Host "Next step: open Installer\VortexLauncher.iss in Inno Setup." -ForegroundColor Cyan
