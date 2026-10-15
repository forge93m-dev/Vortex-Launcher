param(
    [string]$ReleaseDirectory = (Join-Path $PSScriptRoot '../release'),
    [string]$Repository = 'forge93m-dev/Vortex-Launcher',
    [switch]$AllowPartial
)
$ErrorActionPreference = 'Stop'
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../Vortex Launcher.csproj')
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$' -or $Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid release version or repository.' }
$releasePath = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$platforms = [ordered]@{}
$packages = [ordered]@{}
foreach ($rid in @('win-x64', 'osx-arm64', 'osx-x64')) {
    $name = "VortexLauncher-$version-$rid.zip"
    $path = Join-Path $releasePath $name
    if (!(Test-Path -LiteralPath $path)) { continue }
    if ($rid.StartsWith('osx-')) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            if (!$archive.GetEntry('Vortex Launcher.app/Contents/_CodeSignature/CodeResources')) {
                throw "The $rid ZIP has no bundle signature resources. Build/sign it on macOS before creating a release manifest."
            }
        } finally { $archive.Dispose() }
    }
    $url = "https://github.com/$Repository/releases/download/v$version/$name"
    $platforms[$rid] = $url
    $packages[$rid] = [ordered]@{ url=$url; sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant(); size=(Get-Item -LiteralPath $path).Length; kind=$(if ($rid.StartsWith('osx-')) { 'mac-app-zip' } else { 'windows-singlefile-zip' }) }
}
if ($packages.Count -eq 0) { throw 'No release ZIPs found. Build packages first.' }
if (!$AllowPartial -and !$packages.Contains('osx-arm64')) { throw 'A signed Apple Silicon package is required. Run the macOS signing workflow; do not publish another Windows-only manifest.' }
$legacyInstaller = Join-Path $releasePath "VortexLauncherSetup-$version.exe"
$legacyUrl = if (Test-Path -LiteralPath $legacyInstaller) { "https://github.com/$Repository/releases/download/v$version/VortexLauncherSetup-$version.exe" } else { '' }
# Keep legacy platform URLs, but the new updater uses the checksummed packages.
if ($legacyUrl) { $platforms['win-x64'] = $legacyUrl }
$manifest = [ordered]@{ version=$version; url=$legacyUrl; platforms=$platforms; packages=$packages; notes='Launcher self-updates, platform-specific packages, download verification and preserved worlds/settings. Vortex Client 0.1.21 is bundled.' }
$target = Join-Path $releasePath $(if ($AllowPartial) { 'latest.partial.json' } else { 'latest.json' })
[IO.File]::WriteAllText($target, ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Host "Prepared manifest: $target. Nothing has been published." -ForegroundColor Green
