param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '../bin/Debug/net8.0/Vortex Launcher.dll'),
    [string]$MinecraftMetadataPath
)

# Run with PowerShell 7 after building the launcher. Fixtures stay in a new
# temporary directory; this never launches Java or changes real instances.
$ErrorActionPreference = 'Stop'
$launcherAssembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$launcherType = $launcherAssembly.GetType('VortexLauncher.Core.Launch.GameLauncher', $true)
$method = $launcherType.GetMethod('ApplyStackShadowPagesRequirement',
    [Reflection.BindingFlags]'NonPublic, Static')
if (-not $method) { throw 'The launcher assembly does not contain the stack shadow fix.' }

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('vortex-jvm-tests-' + [guid]::NewGuid().ToString('N'))
$fixtureDirectory = Join-Path $fixtureRoot 'versions/test'
New-Item -ItemType Directory -Path $fixtureDirectory | Out-Null
$fixtureMetadata = Join-Path $fixtureDirectory 'test.json'
$script:passed = 0

function Test-Arguments {
    param(
        [string]$Name,
        [string]$Metadata,
        [string[]]$InputArguments,
        [string[]]$ExpectedArguments
    )
    if ([string]::IsNullOrEmpty($Metadata)) {
        if (Test-Path -LiteralPath $fixtureMetadata) {
            Remove-Item -LiteralPath $fixtureMetadata
        }
    } else {
        [IO.File]::WriteAllText($fixtureMetadata, $Metadata)
    }
    $arguments = [Text.StringBuilder]::new()
    foreach ($argument in $InputArguments) { [void]$arguments.AppendLine($argument) }
    [void]$method.Invoke($null, @($arguments.PSObject.BaseObject, $fixtureRoot.PSObject.BaseObject, 'test'))
    $actual = @($arguments.ToString().Split("`n", [StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object { $_.TrimEnd("`r") })
    if (($actual -join "`n") -cne ($ExpectedArguments -join "`n")) {
        throw "$Name failed. Expected: $($ExpectedArguments -join ', '); actual: $($actual -join ', ')"
    }
    $script:passed++
    Write-Host "PASS: $Name"
}

$modernMetadata = '{"arguments":{"jvm":["-XX:StackShadowPages=32"]}}'
Test-Arguments 'Vanilla 26.3: adds the required option' $modernMetadata @('-Xmx4G') @('-Xmx4G', '-XX:StackShadowPages=32')
Test-Arguments 'Fabric: keeps loader and classpath arguments' $modernMetadata @('-Dfabric.development=false', '-cp', 'C:/Test Folder/libraries.jar', '-Xmx4G') @('-Dfabric.development=false', '-cp', 'C:/Test Folder/libraries.jar', '-Xmx4G', '-XX:StackShadowPages=32')
Test-Arguments 'Manual 32: no duplicate' $modernMetadata @('-XX:StackShadowPages=32') @('-XX:StackShadowPages=32')
Test-Arguments 'Forge and manual duplicates: merged' $modernMetadata @('-XX:StackShadowPages=32', '-Xmx4G', '-XX:StackShadowPages=32') @('-Xmx4G', '-XX:StackShadowPages=32')
Test-Arguments 'Manual lower value: cannot undo the requirement' $modernMetadata @('-XX:StackShadowPages=20') @('-XX:StackShadowPages=32')
Test-Arguments 'Manual higher value: preserved' $modernMetadata @('-XX:StackShadowPages=64') @('-XX:StackShadowPages=64')
Test-Arguments 'Quoted manual value: merged' $modernMetadata @('"-XX:StackShadowPages=32"') @('-XX:StackShadowPages=32')
Test-Arguments 'Older Minecraft: no new option' '{"arguments":{"jvm":["-Djava.library.path=${natives_directory}","-cp","${classpath}"]}}' @('-Xmx4G') @('-Xmx4G')
Test-Arguments 'Older Minecraft: preserves manual settings' '{}' @('-XX:StackShadowPages=64', '-Xmx4G') @('-XX:StackShadowPages=64', '-Xmx4G')
Test-Arguments 'Future metadata: uses the declared value' '{"arguments":{"jvm":["-XX:StackShadowPages=48"]}}' @('-Xmx4G') @('-Xmx4G', '-XX:StackShadowPages=48')
Test-Arguments 'Repeated metadata values: uses the largest requirement' '{"arguments":{"jvm":["-XX:StackShadowPages=32","-XX:StackShadowPages=48"]}}' @() @('-XX:StackShadowPages=48')
Test-Arguments 'Disallowed platform rule: ignored' '{"arguments":{"jvm":[{"rules":[{"action":"allow","os":{"name":"unsupported-platform"}}],"value":"-XX:StackShadowPages=64"}]}}' @('-Xmx4G') @('-Xmx4G')
Test-Arguments 'Allowed object with array value: honored' '{"arguments":{"jvm":[{"rules":[{"action":"allow"}],"value":["-XX:StackShadowPages=32"]}]}}' @() @('-XX:StackShadowPages=32')
Test-Arguments 'Zero metadata value: no new option' '{"arguments":{"jvm":["-XX:StackShadowPages=0"]}}' @('-Xmx4G') @('-Xmx4G')
Test-Arguments 'Malformed metadata value: no invalid option' '{"arguments":{"jvm":["-XX:StackShadowPages=invalid"]}}' @('-Xmx4G') @('-Xmx4G')
Test-Arguments 'Missing version file: preserves the existing launch' $null @('-Xmx4G') @('-Xmx4G')

if ($MinecraftMetadataPath) {
    Test-Arguments 'Installed Minecraft metadata: automatic requirement without custom flags' ([IO.File]::ReadAllText((Resolve-Path -LiteralPath $MinecraftMetadataPath).Path)) @('-Xmx4G') @('-Xmx4G', '-XX:StackShadowPages=32')
}
Write-Host "$script:passed tests passed. Temporary fixtures: $fixtureRoot"
