param([Parameter(Mandatory=$true)][string]$InstancePath, [string]$JdkPath = 'C:/Program Files/Java/jdk-25.0.2')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$instance = (Resolve-Path -LiteralPath $InstancePath).Path
$fixture = New-Item -ItemType Directory -Path (Join-Path $env:TEMP ('vortex-party-test-' + [Guid]::NewGuid().ToString('N')))
$gson = Get-ChildItem -LiteralPath (Join-Path $instance 'libraries') -Filter 'gson-*.jar' -Recurse -File | Select-Object -First 1
if (!$gson) { throw 'Downloaded Gson library is required.' }
$classpath = @((Join-Path $root 'Client/VortexClient/dist/vortex-client-26.3-fabric-0.1.23.jar'), $gson.FullName, $fixture.FullName) -join [IO.Path]::PathSeparator
& (Join-Path $JdkPath 'bin/javac.exe') --release 25 -classpath $classpath -d $fixture.FullName (Join-Path $root 'Client/VortexClient/tests/PartyTransportSmoke.java')
if ($LASTEXITCODE -ne 0) { throw 'Java transport test compilation failed.' }
dotnet run --project (Join-Path $PSScriptRoot 'PartySmoke.csproj') -p:UseSharedCompilation=false -- (Join-Path $root 'Services/VortexParty/bin/Debug/net8.0/VortexParty.dll') (Join-Path $JdkPath 'bin/java.exe') $classpath
if ($LASTEXITCODE -ne 0) { throw 'Party tests failed.' }
