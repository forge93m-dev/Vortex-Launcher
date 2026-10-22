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
if (!$AllowPartial) {
    foreach ($requiredRid in @('win-x64','osx-arm64','osx-x64')) {
        if (!$packages.Contains($requiredRid)) { throw "A verified $requiredRid package is required for a complete release. Build Windows and sign both Mac architectures first." }
    }
}
$legacyInstaller = Join-Path $releasePath "VortexLauncherSetup-$version.exe"
$legacyUrl = if (Test-Path -LiteralPath $legacyInstaller) { "https://github.com/$Repository/releases/download/v$version/VortexLauncherSetup-$version.exe" } else { '' }
# Keep legacy platform URLs, but the new updater uses the checksummed packages.
if ($legacyUrl) { $platforms['win-x64'] = $legacyUrl }
$clientMetadata = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../Client/VortexClient/src/main/resources/fabric.mod.json') -Raw | ConvertFrom-Json
$manifest = [ordered]@{ version=$version; url=$legacyUrl; platforms=$platforms; packages=$packages; notes="Vortex Launcher $version - Compact vertically scrolling library with direct Play/Manage actions, shared Minecraft assets and libraries, remembered verification for unchanged assets, parallel content downloads and clearer client install/update buttons. Includes Vortex Client $($clientMetadata.version): Resource Counter, remembered Storage Finder with a temporary marker, clearer labeled menus and animation fixes. Exact Fabric builds for 26.1-26.3 and 1.21.11; ports remain experimental. Existing instances: use Update Vortex Client. Worlds, mods and settings are preserved. Mac ARM64/Intel packages are ad-hoc signed, not notarized; Open Anyway may be required." }
$target = Join-Path $releasePath $(if ($AllowPartial) { 'latest.partial.json' } else { 'latest.json' })
[IO.File]::WriteAllText($target, ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Host "Prepared manifest: $target. Nothing has been published." -ForegroundColor Green
