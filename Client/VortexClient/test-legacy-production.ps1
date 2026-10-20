param(
    [string]$JdkPath = 'C:/Program Files/Eclipse Adoptium/jdk-25.0.4.101-hotspot'
)
$ErrorActionPreference = 'Stop'
$targetRoot = Join-Path $PSScriptRoot 'build/targets/1.21.11'
$metadata = Invoke-RestMethod 'https://piston-meta.mojang.com/mc/game/version_manifest_v2.json'
$versionEntry = $metadata.versions | Where-Object id -eq '1.21.11' | Select-Object -First 1
$gameMetadata = Invoke-RestMethod $versionEntry.url
$game = Join-Path $targetRoot 'minecraft.jar'
if (!(Test-Path -LiteralPath $game) -or (Get-FileHash -LiteralPath $game -Algorithm SHA1).Hash -ne $gameMetadata.downloads.client.sha1) {
    Invoke-WebRequest $gameMetadata.downloads.client.url -OutFile $game
    if ((Get-FileHash -LiteralPath $game -Algorithm SHA1).Hash -ne $gameMetadata.downloads.client.sha1) { throw 'Invalid game JAR.' }
}
$mappings = Join-Path $targetRoot 'intermediary-1.21.11-v2.jar'
$mappingUrl = 'https://maven.fabricmc.net/net/fabricmc/intermediary/1.21.11/intermediary-1.21.11-v2.jar'
$mappingSha = (Invoke-RestMethod ($mappingUrl + '.sha1')).Trim()
if (!(Test-Path -LiteralPath $mappings) -or (Get-FileHash -LiteralPath $mappings -Algorithm SHA1).Hash -ne $mappingSha) {
    Invoke-WebRequest $mappingUrl -OutFile $mappings
    if ((Get-FileHash -LiteralPath $mappings -Algorithm SHA1).Hash -ne $mappingSha) { throw 'Invalid game mappings.' }
}
$candidate = Join-Path $PSScriptRoot 'ports/1.21.11/build/libs/vortex-client-1.21.11-fabric-0.2.1.jar'
if (!(Test-Path -LiteralPath $candidate)) { throw 'Build the remapped 1.21.11 client with Loom before testing.' }
$libraries = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ports/1.21.11/build/port-classpath.txt') -Raw).Trim().Split([IO.Path]::PathSeparator) |
    Where-Object { $_ -notmatch '[\\/]minecraftMaven[\\/]net[\\/]minecraft[\\/]' }
$smokeRoot = Join-Path $targetRoot ('production-' + [Guid]::NewGuid().ToString('N'))
$classes = Join-Path $smokeRoot 'classes'
New-Item -ItemType Directory -Path $classes,(Join-Path $smokeRoot 'mods') -Force | Out-Null
Copy-Item -LiteralPath $candidate -Destination (Join-Path $smokeRoot 'mods')
$classpath = (@($classes,$game,$mappings) + @($libraries)) -join [IO.Path]::PathSeparator
& (Join-Path $JdkPath 'bin/javac.exe') --release 21 -proc:none -classpath $classpath -d $classes (Join-Path $PSScriptRoot 'tests/TargetMixinSmoke.java') (Join-Path $PSScriptRoot 'tests/CollectionSmoke.java')
if ($LASTEXITCODE -ne 0) { throw 'Production test compilation failed.' }
Push-Location $smokeRoot
try {
    & (Join-Path $JdkPath 'bin/java.exe') '--enable-native-access=ALL-UNNAMED' '-XX:StackShadowPages=32' '-Xmx1G' '-Dfabric.gameVersion=1.21.11' -classpath $classpath TargetMixinSmoke $smokeRoot '1.21.11' $candidate
    if ($LASTEXITCODE -ne 0) { throw 'Production intermediary mixin test failed. This port is not installable yet.' }
} finally { Pop-Location }
Write-Host 'LEGACY_PRODUCTION_OK 1.21.11. Full GUI/gameplay testing is still required.'
