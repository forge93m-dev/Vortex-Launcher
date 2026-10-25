param(
    [Parameter(Mandatory=$true)][string]$InstancePath,
    [string]$JdkPath = 'C:/Program Files/Java/jdk-25.0.2'
)
$ErrorActionPreference = 'Stop'
$gameJar = Join-Path $InstancePath '26.3.jar'
if (!(Test-Path -LiteralPath $gameJar)) { throw 'A downloaded Minecraft 26.3 instance is required.' }
$libraries = @(Get-ChildItem -LiteralPath (Join-Path $InstancePath 'libraries') -Recurse -Filter '*.jar' -File)
if (!($libraries | Where-Object Name -eq 'fabric-loader-0.19.5.jar')) { throw 'Fabric Loader 0.19.5 is required for this build.' }
$classPath = (@($gameJar) + @($libraries.FullName)) -join [IO.Path]::PathSeparator
$buildDir = Join-Path $PSScriptRoot ('build/local-' + [Guid]::NewGuid().ToString('N'))
$distDir = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $buildDir,$distDir -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src/main/java') -Recurse -Filter '*.java' -File)
# Minecraft 26.3 is unobfuscated, so no remapping or refmap is needed.
# Use the installed official game/Fabric dependencies, but never package them.
& (Join-Path $JdkPath 'bin/javac.exe') --release 25 -proc:none -encoding UTF-8 -classpath $classPath -d $buildDir @($sources.FullName)
if ($LASTEXITCODE -ne 0) { throw 'Vortex Client compilation failed.' }
$jarPath = Join-Path $distDir 'vortex-client-26.3-fabric-0.2.6.jar'
& (Join-Path $JdkPath 'bin/jar.exe') --create --date '2026-10-14T00:00:00Z' --file $jarPath -C $buildDir . -C (Join-Path $PSScriptRoot 'src/main/resources') .
if ($LASTEXITCODE -ne 0) { throw 'Vortex Client packaging failed.' }
Write-Host "Built: $jarPath"
