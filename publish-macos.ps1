param(
    [ValidateSet('osx-arm64', 'osx-x64', 'all')][string]$Runtime = 'all',
    [string]$SignIdentity = '-',
    [switch]$Notarize
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$projectPath = Join-Path $PSScriptRoot 'Vortex Launcher.csproj'
[xml]$projectXml = Get-Content -LiteralPath $projectPath
$appVersion = [string]$projectXml.Project.PropertyGroup.Version
if ($Notarize -and (!$IsMacOS -or $SignIdentity -eq '-')) { throw 'Notarization requires a Mac and a Developer ID Application signing identity.' }
$targets = if ($Runtime -eq 'all') { @('osx-arm64', 'osx-x64') } else { @($Runtime) }
foreach ($target in $targets) {
    $outputPath = Join-Path $PSScriptRoot "publish/$target"
    # Build into a fresh staging folder; never include stale files from an older app bundle.
    $packageBuildPath = Join-Path $outputPath ("package-build-$appVersion-" + [Guid]::NewGuid().ToString('N'))
    $bundlePath = Join-Path $packageBuildPath 'Vortex Launcher.app'
    $binaryPath = Join-Path $bundlePath 'Contents/MacOS'
    New-Item -ItemType Directory -Path $binaryPath -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $bundlePath 'Contents/Resources') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets/Branding/vortex-icon-v1.icns') -Destination (Join-Path $bundlePath 'Contents/Resources/vortex-icon-v1.icns')
    dotnet publish $projectPath -c Release -r $target --self-contained true -o $binaryPath `
        -p:UseAppHost=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "Publication failed for $target (exit code $LASTEXITCODE)." }
    $plist = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Installer/macOS/Info.plist')).Replace('__VERSION__', $appVersion)
    [IO.File]::WriteAllText((Join-Path $bundlePath 'Contents/Info.plist'), $plist, [Text.UTF8Encoding]::new($false))

    if ($IsMacOS) {
        & /bin/chmod 755 (Join-Path $binaryPath 'Vortex Launcher')
        if ($LASTEXITCODE -ne 0) { throw "Cannot mark the launcher executable for $target." }
        $signArguments = @('--force', '--entitlements', (Join-Path $PSScriptRoot 'Installer/macOS/Entitlements.plist'), '--sign', $SignIdentity)
        if ($SignIdentity -ne '-') { $signArguments += @('--options', 'runtime', '--timestamp') }
        & /usr/bin/codesign @signArguments $bundlePath
        if ($LASTEXITCODE -ne 0) { throw "App signing failed for $target." }
        & /usr/bin/codesign --verify --deep --strict $bundlePath
        if ($LASTEXITCODE -ne 0) { throw "App signature verification failed for $target." }
        $hostRid = if ([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq [Runtime.InteropServices.Architecture]::Arm64) { 'osx-arm64' } else { 'osx-x64' }
        if ($target -eq $hostRid) {
            $reportedVersion = & (Join-Path $binaryPath 'Vortex Launcher') --launcher-version
            if ($LASTEXITCODE -ne 0 -or ($reportedVersion -join '') -notmatch ([regex]::Escape("$appVersion $target"))) { throw 'Signed macOS executable failed its CLI startup check.' }
        }
        $releaseDirectory = Join-Path $PSScriptRoot 'release'
        New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
        $signedZip = Join-Path $releaseDirectory "VortexLauncher-$appVersion-$target.zip"
        if (Test-Path -LiteralPath $signedZip) { throw "A release package already exists at $signedZip. Preserve it or select a clean release directory before rebuilding." }
        & /usr/bin/ditto -c -k --keepParent $bundlePath $signedZip
        if ($LASTEXITCODE -ne 0) { throw "macOS packaging failed for $target." }
        if ($Notarize) {
            if (!$env:APPLE_ID -or !$env:APPLE_TEAM_ID -or !$env:APPLE_APP_PASSWORD) { throw 'Apple notarization credentials must be provided through secure environment variables.' }
            $notaryResult = & /usr/bin/xcrun notarytool submit $signedZip --apple-id $env:APPLE_ID --team-id $env:APPLE_TEAM_ID --password $env:APPLE_APP_PASSWORD --wait --output-format json
            if ($LASTEXITCODE -ne 0) { throw "Notarization failed for $target." }
            if (($notaryResult | ConvertFrom-Json).status -ne 'Accepted') { throw "Apple did not accept the notarization request for $target." }
            & /usr/bin/xcrun stapler staple $bundlePath
            if ($LASTEXITCODE -ne 0) { throw "Stapling failed for $target." }
            # A second, separate ZIP contains the stapled app. Retain the submitted ZIP for diagnostics.
            $stapledZip = Join-Path $outputPath "VortexLauncher-$appVersion-$target-notarized.zip"
            & /usr/bin/ditto -c -k --keepParent $bundlePath $stapledZip
            if ($LASTEXITCODE -ne 0) { throw "Notarized packaging failed for $target." }
            Copy-Item -LiteralPath $stapledZip -Destination $signedZip -Force
        }
        # Verify the exact ZIP users receive, not only the pre-packaging bundle.
        $verifyDirectory = Join-Path $packageBuildPath 'zip-verification'
        New-Item -ItemType Directory -Path $verifyDirectory | Out-Null
        & /usr/bin/ditto -x -k $signedZip $verifyDirectory
        if ($LASTEXITCODE -ne 0) { throw "ZIP extraction check failed for $target." }
        $verifiedBundle = Join-Path $verifyDirectory 'Vortex Launcher.app'
        & /usr/bin/codesign --verify --deep --strict $verifiedBundle
        if ($LASTEXITCODE -ne 0) { throw "The packaged app signature is invalid for $target." }
        $verifiedBinary = Join-Path $verifiedBundle 'Contents/MacOS/Vortex Launcher'
        if (([IO.File]::GetUnixFileMode($verifiedBinary) -band [IO.UnixFileMode]::UserExecute) -eq 0) { throw "The packaged launcher lost its executable permission for $target." }
        if ($target -eq $hostRid) {
            $reportedVersion = & $verifiedBinary --launcher-version
            if ($LASTEXITCODE -ne 0 -or ($reportedVersion -join '').Trim() -ne "$appVersion $target") { throw 'The extracted macOS executable failed its CLI startup check.' }
        }
        if ($Notarize) {
            & /usr/bin/xcrun stapler validate $verifiedBundle
            if ($LASTEXITCODE -ne 0) { throw "The packaged app lost its notarization ticket for $target." }
        }
        $verification = [ordered]@{
            version = $appVersion
            runtime = $target
            signing = $(if ($SignIdentity -eq '-') { 'ad-hoc-beta' } else { 'developer-id' })
            notarized = $Notarize.IsPresent
            zip_signature_verified = $true
            executable_permission_verified = $true
            native_cli_startup_verified = ($target -eq $hostRid)
            sha256 = (Get-FileHash -LiteralPath $signedZip -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        [IO.File]::WriteAllText(($signedZip + '.verification.json'), ($verification | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
        Write-Host "Signed macOS package: $signedZip" -ForegroundColor Green
        continue
    }

    # ZIP central-directory Unix attributes preserve the executable bit even
    # when this script runs on Windows. Do not use Explorer to re-zip the app.
    $archivePath = Join-Path $outputPath "VortexLauncher-$appVersion-$target.zip"
    $stream = [IO.File]::Open($archivePath, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite)
    try {
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($file in Get-ChildItem -LiteralPath $bundlePath -Recurse -File) {
                $relative = $file.FullName.Substring($packageBuildPath.Length + 1).Replace('\', '/')
                $entry = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal)
                $mode = if ($file.Name -eq 'Vortex Launcher' -or $file.Extension -eq '.dylib') { 0x81ED } else { 0x81A4 }
                $entry.ExternalAttributes = $mode -shl 16
            }
        } finally { $zip.Dispose() }
        $stream.Position = $stream.Length - 22
        $end = New-Object byte[] 22
        if ($stream.Read($end, 0, 22) -ne 22 -or [BitConverter]::ToUInt32($end, 0) -ne 0x06054b50) { throw 'Invalid ZIP end record.' }
        $stream.Position = [BitConverter]::ToUInt32($end, 16)
        $entryCount = [BitConverter]::ToUInt16($end, 10)
        for ($index = 0; $index -lt $entryCount; $index++) {
            $position = $stream.Position
            $header = New-Object byte[] 46
            if ($stream.Read($header, 0, 46) -ne 46 -or [BitConverter]::ToUInt32($header, 0) -ne 0x02014b50) { throw 'Invalid ZIP directory entry.' }
            $stream.Position = $position + 5
            $stream.WriteByte(3) # ZIP creator OS: Unix
            $stream.Position = $position + 46 + [BitConverter]::ToUInt16($header, 28) + [BitConverter]::ToUInt16($header, 30) + [BitConverter]::ToUInt16($header, 32)
        }
    } finally { $stream.Dispose() }
    Write-Host "macOS package: $archivePath" -ForegroundColor Green
}
if (!$IsMacOS) { Write-Host 'Windows cross-builds are UNSIGNED. Do not publish them as updater packages; use the macOS signing workflow first. See MACOS.md.' -ForegroundColor Yellow }
elseif (!$Notarize) { Write-Host 'Valid beta signature only; macOS may require the GUI Open Anyway approval. Developer ID + notarization are required for normal public distribution.' -ForegroundColor Yellow }
