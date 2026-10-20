param(
    [Parameter(Mandatory=$true)][string]$InstancePath,
    [string]$JdkPath = 'C:/Program Files/Java/jdk-25.0.2',
    [string]$PhotoPath = ''
)
$ErrorActionPreference = 'Stop'
$instance = (Resolve-Path -LiteralPath $InstancePath).Path
$javaBin = Join-Path $JdkPath 'bin'
$testRoot = Join-Path $env:TEMP ('vortex-fabric-smoke-' + [Guid]::NewGuid().ToString('N'))
$classes = Join-Path $testRoot 'classes'
$mods = Join-Path $testRoot 'mods'
New-Item -ItemType Directory -Path $classes,$mods -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dist/vortex-client-26.3-fabric-0.2.1.jar') -Destination $mods
$libraries = @(Get-ChildItem -LiteralPath (Join-Path $instance 'libraries') -Recurse -Filter '*.jar' -File)
$classpath = (@((Join-Path $instance '26.3.jar'),$classes) + @($libraries.FullName)) -join [IO.Path]::PathSeparator
& (Join-Path $javaBin 'javac.exe') --release 25 -proc:none -classpath $classpath -d $classes (Join-Path $PSScriptRoot 'tests/FabricSmoke.java') (Join-Path $PSScriptRoot 'tests/CapeSmoke.java') (Join-Path $PSScriptRoot 'tests/PhotoSmoke.java') (Join-Path $PSScriptRoot 'tests/WardrobeSmoke.java') (Join-Path $PSScriptRoot 'tests/CollectionSmoke.java') (Join-Path $PSScriptRoot 'tests/MotionSmoke.java') (Join-Path $PSScriptRoot 'tests/WardrobeQoLSmoke.java') (Join-Path $PSScriptRoot 'tests/NotificationSmoke.java') (Join-Path $PSScriptRoot 'tests/ChestSearchSmoke.java') (Join-Path $PSScriptRoot 'tests/WaypointsSmoke.java') (Join-Path $PSScriptRoot 'tests/InventoryHealthSmoke.java') (Join-Path $PSScriptRoot 'tests/AtlasQoLSmoke.java')
if ($LASTEXITCODE -ne 0) { throw 'Smoke test compilation failed.' }
Push-Location $testRoot
try {
    $photoArguments = @()
    if ($PhotoPath) { $photoArguments = @("-Dvortex.testPhoto=$PhotoPath") }
    & (Join-Path $javaBin 'java.exe') '--enable-native-access=ALL-UNNAMED' '-XX:StackShadowPages=32' '-Xmx1G' '-Dfabric.gameVersion=26.3' @photoArguments -classpath $classpath FabricSmoke $testRoot
    if ($LASTEXITCODE -ne 0) { throw 'Fabric transformation smoke test failed.' }
} finally { Pop-Location }
