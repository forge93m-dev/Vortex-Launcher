param([Parameter(Mandatory=$true)][string]$Version,[Parameter(Mandatory=$true)][string]$BuildCommit)
$ErrorActionPreference='Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or $BuildCommit -notmatch '^[a-f0-9]{40}$' -or $env:GITHUB_REPOSITORY -ne 'forge93m-dev/Vortex-Launcher') {throw 'Unexpected release identity.'}
$repository=$env:GITHUB_REPOSITORY
$tag="v$Version"
$manifestPath=Join-Path $PSScriptRoot '../release/latest.json'
$releasePath=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../release')).Path
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.version -ne $Version) {throw 'Artifact version does not match requested version.'}
foreach ($rid in @('win-x64','osx-arm64','osx-x64')) {
    $spec=$manifest.packages.$rid
    $name="VortexLauncher-$Version-$rid.zip"
    if ($spec.url -ne "https://github.com/$repository/releases/download/$tag/$name") {throw "Invalid package URL for $rid"}
    $path=Join-Path $releasePath $name
    if ((Get-Item -LiteralPath $path).Length -ne $spec.size -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $spec.sha256) {throw "Invalid package bytes for $rid"}
    if ($rid.StartsWith('osx-')) {
        $proof=Get-Content -LiteralPath ($path+'.verification.json') -Raw | ConvertFrom-Json
        if ($proof.version -ne $Version -or $proof.runtime -ne $rid -or !$proof.zip_signature_verified -or !$proof.executable_permission_verified -or !$proof.native_cli_startup_verified -or $proof.sha256 -ne $spec.sha256) {throw "Missing native signature/startup evidence for $rid"}
    }
    Write-Output "VERIFIED: $rid bytes, checksum and native evidence"
}
$installer=Join-Path $releasePath "VortexLauncherSetup-$Version.exe"
if (!(Test-Path -LiteralPath $installer) -or $manifest.url -ne "https://github.com/$repository/releases/download/$tag/VortexLauncherSetup-$Version.exe") {throw 'Legacy installer is missing or misrouted.'}
$draft=gh release view $tag --repo $repository --json databaseId,isDraft | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or !$draft.isDraft) {throw 'A matching draft release is required; published releases are never overwritten.'}
$files=@(Get-ChildItem -LiteralPath $releasePath -File)
foreach ($file in $files) {
    if ($file.Name -notmatch ('^(VortexLauncher-'+[regex]::Escape($Version)+'-(win-x64|osx-arm64|osx-x64)\.zip(\.verification\.json)?|VortexLauncherSetup-'+[regex]::Escape($Version)+'\.exe|latest\.json|OPEN-FIRST\.txt|RELEASE-NOTES-'+[regex]::Escape($Version)+'\.md)$')) {throw 'Unexpected file in prepared release.'}
}
gh release upload $tag --repo $repository --clobber @($files.FullName)
if ($LASTEXITCODE -ne 0) {throw 'Release asset upload failed; manifest unchanged.'}
$uploaded=gh api "repos/$repository/releases/$($draft.databaseId)" | ConvertFrom-Json
foreach ($rid in @('win-x64','osx-arm64','osx-x64')) {
    $name="VortexLauncher-$Version-$rid.zip"
    $asset=@($uploaded.assets | Where-Object name -eq $name)
    $spec=$manifest.packages.$rid
    if ($asset.Count -ne 1 -or $asset[0].state -ne 'uploaded' -or $asset[0].size -ne $spec.size -or $asset[0].digest -ne ('sha256:'+$spec.sha256)) {throw "Uploaded asset verification failed: $rid; manifest unchanged."}
}
gh release edit $tag --repo $repository --target $BuildCommit --draft=false --latest
if ($LASTEXITCODE -ne 0) {throw 'Could not publish the verified draft; manifest unchanged.'}
# Check anonymous public downloads BEFORE telling installed launchers about the update.
foreach ($rid in @('win-x64','osx-arm64','osx-x64')) {
    $spec=$manifest.packages.$rid
    $response=Invoke-WebRequest -Uri $spec.url -Method Head -MaximumRetryCount 3 -RetryIntervalSec 3
    if ($response.StatusCode -ne 200 -or [long]$response.Headers.'Content-Length'[0] -ne $spec.size) {throw "Public package is not ready: $rid; manifest unchanged."}
}
$current=gh api "repos/$repository/contents/latest.json?ref=main" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or !$current.sha) {throw 'Cannot read current update feed.'}
$oldText=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($current.content))
$old=$oldText | ConvertFrom-Json
if ([version]$old.version -gt [version]$Version) {throw 'Refusing to downgrade the update feed.'}
$request=[ordered]@{message="Publish Vortex Launcher $Version update feed [skip ci]";sha=$current.sha;branch='main';content=[Convert]::ToBase64String([IO.File]::ReadAllBytes($manifestPath))}
$requestPath=Join-Path $env:RUNNER_TEMP 'vortex-update-feed-request.json'
[IO.File]::WriteAllText($requestPath,($request | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
gh api --method PUT "repos/$repository/contents/latest.json" --input $requestPath --jq '.commit.html_url'
if ($LASTEXITCODE -ne 0) {throw 'Release is published but feed commit failed; retry the feed separately.'}
$feed=gh api "repos/$repository/contents/latest.json?ref=main" | ConvertFrom-Json
$published=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($feed.content)) | ConvertFrom-Json
if ($published.version -ne $Version) {throw 'Published update feed readback failed.'}
Write-Output "PUBLISHED: https://github.com/$repository/releases/tag/$tag; installed-launcher feed now $Version"
