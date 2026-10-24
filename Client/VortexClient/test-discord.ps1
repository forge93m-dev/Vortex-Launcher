param(
    [Parameter(Mandatory=$true)][string]$InstancePath,
    [string]$JdkPath = 'C:/Program Files/Eclipse Adoptium/jdk-25.0.4.101-hotspot',
    [switch]$DesktopProbe
)
$ErrorActionPreference = 'Stop'
$gson = Get-ChildItem -LiteralPath (Join-Path $InstancePath 'libraries') -Recurse -Filter 'gson-*.jar' -File | Select-Object -First 1
if (!$gson) { throw 'An existing Minecraft instance with Gson is required.' }
$jna = Get-ChildItem -LiteralPath (Join-Path $InstancePath 'libraries') -Recurse -Filter 'jna-*.jar' -File | Where-Object Name -Match '^jna-[0-9]' | Select-Object -First 1
if (!$jna) { throw 'Minecraft JNA runtime is required.' }
$discordDependencies = $gson.FullName + [IO.Path]::PathSeparator + $jna.FullName
$testRoot = Join-Path $env:TEMP ('vortex-discord-smoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
& (Join-Path $JdkPath 'bin/javac.exe') --release 21 -encoding UTF-8 -classpath $discordDependencies -d $testRoot (Join-Path $PSScriptRoot 'src/main/java/dev/vortex/client/ServerInvite.java') (Join-Path $PSScriptRoot 'src/main/java/dev/vortex/client/WindowsDiscordPipe.java') (Join-Path $PSScriptRoot 'src/main/java/dev/vortex/client/DiscordRpc.java') (Join-Path $PSScriptRoot 'tests/DiscordRpcSmoke.java')
if ($LASTEXITCODE -ne 0) { throw 'Discord test compilation failed.' }
& (Join-Path $JdkPath 'bin/java.exe') -classpath ($testRoot + [IO.Path]::PathSeparator + $discordDependencies) dev.vortex.client.DiscordRpcSmoke
if ($LASTEXITCODE -ne 0) { throw 'Discord protocol tests failed.' }
if ($DesktopProbe) {
    & (Join-Path $JdkPath 'bin/javac.exe') --release 21 -encoding UTF-8 -classpath ($testRoot + [IO.Path]::PathSeparator + $discordDependencies) -d $testRoot (Join-Path $PSScriptRoot 'tests/DiscordIpcProbe.java')
    if ($LASTEXITCODE -ne 0) { throw 'Native IPC probe compilation failed.' }
    & (Join-Path $JdkPath 'bin/java.exe') -classpath ($testRoot + [IO.Path]::PathSeparator + $discordDependencies) dev.vortex.client.DiscordIpcProbe
    if ($LASTEXITCODE -ne 0) { throw 'Native Discord IPC probe failed.' }
}
