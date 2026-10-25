param(
    [ValidateSet('26.1','26.1.1','26.1.2','26.2','1.21.11')][string]$MinecraftVersion = '26.2',
    [string]$JdkPath = 'C:/Program Files/Eclipse Adoptium/jdk-25.0.4.101-hotspot',
    [string]$ExistingLibraries = '',
    [string]$GradlePath = ''
)
$ErrorActionPreference = 'Stop'
$targetRoot = Join-Path $PSScriptRoot "build/targets/$MinecraftVersion"
New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
$legacy = $MinecraftVersion -eq '1.21.11'
if ($legacy) {
    if (!$GradlePath) {
        $command = Get-Command gradle.bat,gradle -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command) { $GradlePath = $command.Source }
        else {
            $distribution = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.gradle/wrapper/dists/gradle-9.7.1-bin'
            $GradlePath = Get-ChildItem -LiteralPath $distribution -Filter 'gradle.bat' -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
        }
    }
    if (!$GradlePath) { throw 'Gradle 9.7.1 is required for the 1.21.11 port. Pass -GradlePath explicitly.' }
    $legacyProject = Join-Path $PSScriptRoot 'ports/1.21.11'
    & $GradlePath "-Dorg.gradle.java.home=$JdkPath" -p $legacyProject preparePortClasspath --no-daemon
    if ($LASTEXITCODE -ne 0) { throw 'Legacy mapping/classpath preparation failed.' }
}
function Get-VerifiedFile($url, $destination, $sha1) {
    if ((Test-Path -LiteralPath $destination) -and (Get-FileHash -LiteralPath $destination -Algorithm SHA1).Hash -eq $sha1) { return }
    Invoke-WebRequest $url -OutFile ($destination + '.part')
    if ((Get-FileHash -LiteralPath ($destination + '.part') -Algorithm SHA1).Hash -ne $sha1) { throw "Checksum mismatch: $destination" }
    Move-Item -LiteralPath ($destination + '.part') -Destination $destination -Force
}
if (!$legacy) {
$manifest = Invoke-RestMethod 'https://piston-meta.mojang.com/mc/game/version_manifest_v2.json'
$entry = $manifest.versions | Where-Object id -eq $MinecraftVersion | Select-Object -First 1
if (!$entry) { throw 'Official Minecraft version metadata not found.' }
$metadata = Invoke-RestMethod $entry.url
$game = Join-Path $targetRoot 'minecraft.jar'
Get-VerifiedFile $metadata.downloads.client.url $game $metadata.downloads.client.sha1
$libraryRoot = Join-Path $targetRoot 'libraries'
New-Item -ItemType Directory -Path $libraryRoot -Force | Out-Null
$existing = if ($ExistingLibraries) { @(Get-ChildItem -LiteralPath $ExistingLibraries -Filter '*.jar' -Recurse -File) } else { @() }
foreach ($library in $metadata.libraries) {
    $artifact = $library.downloads.artifact
    if (!$artifact) { continue }
    $destination = Join-Path $libraryRoot ([IO.Path]::GetFileName($artifact.path))
    $reuse = $existing | Where-Object Name -eq ([IO.Path]::GetFileName($artifact.path)) | Select-Object -First 1
    if ($reuse -and (Get-FileHash -LiteralPath $reuse.FullName -Algorithm SHA1).Hash -eq $artifact.sha1 -and !(Test-Path -LiteralPath $destination)) {
        Copy-Item -LiteralPath $reuse.FullName -Destination $destination
    }
    Get-VerifiedFile $artifact.url $destination $artifact.sha1
}
$fabric = Invoke-RestMethod "https://meta.fabricmc.net/v2/versions/loader/$MinecraftVersion/0.19.5/profile/json"
foreach ($library in $fabric.libraries) {
    $parts = $library.name.Split(':'); $file = "$($parts[1])-$($parts[2]).jar"
    $url = $library.url.TrimEnd('/') + '/' + $parts[0].Replace('.','/') + "/$($parts[1])/$($parts[2])/$file"
    $sha1 = (Invoke-RestMethod ($url + '.sha1')).ToString().Trim().Split(' ')[0]
    Get-VerifiedFile $url (Join-Path $libraryRoot $file) $sha1
}
}
$generated = Join-Path $targetRoot ('generated-' + [Guid]::NewGuid().ToString('N'))
$classes = Join-Path $targetRoot ('classes-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $generated,$classes -Force | Out-Null
foreach ($source in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src/main/java') -Filter '*.java' -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath((Join-Path $PSScriptRoot 'src/main/java'), $source.FullName)
    $override = Join-Path $PSScriptRoot "ports/$MinecraftVersion/$relative"
    if (!(Test-Path -LiteralPath $override) -and ($legacy -or $MinecraftVersion.StartsWith('26.1'))) { $override = Join-Path $PSScriptRoot "ports/26.1/$relative" }
    if (!(Test-Path -LiteralPath $override)) { $override = Join-Path $PSScriptRoot "ports/26.2/$relative" }
    $content = Get-Content -LiteralPath $(if (Test-Path -LiteralPath $override) { $override } else { $source.FullName }) -Raw
    $content = $content.Replace('com.mojang.renderpearl.api.pipeline','com.mojang.blaze3d.pipeline').Replace('com/mojang/renderpearl/api/pipeline','com/mojang/blaze3d/pipeline')
    $content = $content.Replace('com.mojang.renderpearl.api.textures','com.mojang.blaze3d.textures').Replace('com/mojang/renderpearl/api/textures','com/mojang/blaze3d/textures')
    $content = $content.Replace('InputConstants.Type.KEYBOARD','InputConstants.Type.KEYSYM').Replace('InputConstants.isKeyDown(key)', 'InputConstants.isKeyDown(net.minecraft.client.Minecraft.getInstance().getWindow(), key)')
    if ($legacy -or $MinecraftVersion.StartsWith('26.1')) {
        $content = $content.Replace('.gui.screen()', '.screen').Replace('.gui.setScreen(', '.setScreen(').Replace('.gui.overlay()', '.getOverlay()').Replace('.gui.hud', '.gui')
        $content = $content.Replace('net.minecraft.client.gui.Hud', 'net.minecraft.client.gui.Gui').Replace('Hud.class', 'Gui.class').Replace('Hud.get', 'Gui.get')
        $content = $content.Replace('import net.minecraft.world.entity.EntityTypes;', 'import net.minecraft.world.entity.EntityType;').Replace('EntityTypes.', 'EntityType.')
        $content = $content.Replace('.mainCamera()', '.getMainCamera()')
        $content = $content.Replace('.gui.isHidden()', '.options.hideGui').Replace('mapping.matches(pressed)', 'mapping.matches(new net.minecraft.client.input.KeyEvent(key, 0, 0))')
        $content = $content.Replace('submitArmWithItem', 'renderArmWithItem')
    }
    if ($legacy) {
        $content = $content.Replace('GuiGraphicsExtractor', 'GuiGraphics').Replace('net.minecraft.client.multiplayer.chat.GuiMessage', 'net.minecraft.client.GuiMessage').Replace('net/minecraft/client/multiplayer/chat/GuiMessage', 'net/minecraft/client/GuiMessage')
        $content = $content.Replace('extractRenderState', 'render').Replace('extractBackground', 'renderBackground').Replace('extractPanorama', 'renderPanorama').Replace('extractDeferredSubtitles', 'renderDeferredSubtitles')
        $content = $content.Replace('.centeredText(', '.drawCenteredString(').Replace('.text(', '.drawString(').Replace('.outline(', '.renderOutline(').Replace('.horizontalLine(', '.hLine(').Replace('.verticalLine(', '.vLine(')
        $content = $content.Replace('ClientUi.drawString(', 'ClientUi.text(').Replace('.item(', '.renderItem(').Replace('.itemDecorations(', '.renderItemDecorations(')
        $content = $content.Replace('extractImage(', 'renderImage(').Replace('extractText(', 'renderText(')
        $content = $content.Replace('extractContents(', 'renderContents(').Replace('net.minecraft.client.renderer.state.gui.', 'net.minecraft.client.gui.render.state.')
        $content = $content.Replace('extractWidgetRenderState(', 'renderWidget(').Replace('g.entity(', 'g.submitEntityRenderState(').Replace('.areComponentsBound()', '.isBound()')
        $content = $content.Replace('extractTransparentBackground', 'renderTransparentBackground').Replace('PlayerFaceExtractor.render(', 'PlayerFaceRenderer.draw(').Replace('.addClientSystemMessage(', '.addMessage(').Replace('trimmedMessages.get(index).parent().content()', 'vortex$legacyMessage(index)')
        $content = $content.Replace('extractSlot', 'renderSlot').Replace('extractCrosshair', 'renderCrosshair').Replace('extractEffects', 'renderEffects')
        $content = $content.Replace('message.source() == previous.source() && ', 'message.signature() == null && previous.signature() == null && ')
        $content = $content.Replace('message.signature(), message.source(), message.tag()', 'message.signature(), message.tag()')
        $content = $content.Replace('while (!trimmedMessages.isEmpty() && trimmedMessages.getFirst().parent() == previous) {', 'while (!trimmedMessages.isEmpty() && (removed == 0 || !trimmedMessages.getFirst().endOfEntry())) {')
        $content = $content.Replace('camera.getViewRotationProjectionMatrix(new Matrix4f())', 'PortProjection.matrix(camera, new Matrix4f())')
    }
    $destination = Join-Path $generated $relative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
    [IO.File]::WriteAllText($destination, $content, [Text.UTF8Encoding]::new($false))
}
if ($legacy) {
    foreach ($extra in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'ports/1.21.11/dev') -Filter '*.java' -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath((Join-Path $PSScriptRoot 'ports/1.21.11'), $extra.FullName)
        $destination = Join-Path $generated $relative
        if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot "src/main/java/$relative"))) {
            New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
            Copy-Item -LiteralPath $extra.FullName -Destination $destination -Force
        }
    }
}
$classPath = if ($legacy) { (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ports/1.21.11/build/port-classpath.txt') -Raw).Trim() }
    else { (@($game) + @(Get-ChildItem -LiteralPath $libraryRoot -Filter '*.jar' -File | Select-Object -ExpandProperty FullName)) -join [IO.Path]::PathSeparator }
$sources = @(Get-ChildItem -LiteralPath $generated -Filter '*.java' -Recurse -File | Select-Object -ExpandProperty FullName)
$argumentsFile = Join-Path $targetRoot 'javac.args'
$arguments = @('--release', $(if ($legacy) { '21' } else { '25' }), '-proc:none', '-encoding', 'UTF-8', '-classpath', ('"' + $classPath.Replace('\','/') + '"'), '-d', ('"' + $classes.Replace('\','/') + '"'))
$arguments += @($sources | ForEach-Object { '"' + $_.Replace('\','/') + '"' })
[IO.File]::WriteAllLines($argumentsFile, $arguments, [Text.UTF8Encoding]::new($false))
& (Join-Path $JdkPath 'bin/javac.exe') ('@' + $argumentsFile)
if ($LASTEXITCODE -ne 0) { throw "Minecraft $MinecraftVersion port compilation failed. No client artifact was published." }
$resources = Join-Path $targetRoot ('resources-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $resources -Force | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'src/main/resources/*') -Destination $resources -Recurse
$modMetadata = Get-Content -LiteralPath (Join-Path $resources 'fabric.mod.json') -Raw | ConvertFrom-Json
$modMetadata.depends.minecraft = $MinecraftVersion
if ($legacy) {
    $modMetadata.depends.java = '>=21'
    $mixinPath = Join-Path $resources 'vortex-client.mixins.json'
    $mixinMetadata = Get-Content -LiteralPath $mixinPath -Raw | ConvertFrom-Json
    $mixinMetadata.compatibilityLevel = 'JAVA_21'
    $mixinMetadata.client += 'ProjectionAccessor'
    [IO.File]::WriteAllText($mixinPath, ($mixinMetadata | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}
[IO.File]::WriteAllText((Join-Path $resources 'fabric.mod.json'), ($modMetadata | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
$candidate = Join-Path $targetRoot "vortex-client-$MinecraftVersion-fabric-0.2.6-candidate.jar"
& (Join-Path $JdkPath 'bin/jar.exe') --create --file $candidate -C $classes . -C $resources .
if ($LASTEXITCODE -ne 0) { throw 'Candidate packaging failed.' }
$smokeRoot = Join-Path $targetRoot ('smoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $smokeRoot 'mods') -Force | Out-Null
Copy-Item -LiteralPath $candidate -Destination (Join-Path $smokeRoot 'mods')
$smokeClasses = Join-Path $smokeRoot 'classes'
New-Item -ItemType Directory -Path $smokeClasses -Force | Out-Null
& (Join-Path $JdkPath 'bin/javac.exe') --release 21 -proc:none -classpath $classPath -d $smokeClasses (Join-Path $PSScriptRoot 'tests/TargetMixinSmoke.java') (Join-Path $PSScriptRoot 'tests/CollectionSmoke.java') (Join-Path $PSScriptRoot 'tests/ResourceToolsSmoke.java') (Join-Path $PSScriptRoot 'tests/WaypointsSmoke.java')
if ($LASTEXITCODE -ne 0) { throw 'Target mixin test compilation failed.' }
Push-Location $smokeRoot
try {
    $runtimeOptions = @()
    if ($legacy) { $runtimeOptions += '-Dfabric.development=true' }
    & (Join-Path $JdkPath 'bin/java.exe') '--enable-native-access=ALL-UNNAMED' '-XX:StackShadowPages=32' '-Xmx1G' "-Dfabric.gameVersion=$MinecraftVersion" @runtimeOptions -classpath ($smokeClasses + [IO.Path]::PathSeparator + $classPath) TargetMixinSmoke $smokeRoot $MinecraftVersion $candidate
    if ($LASTEXITCODE -ne 0) { throw "Minecraft $MinecraftVersion mixin verification failed. Candidate remains experimental and is not installed." }
} finally { Pop-Location }
if ($legacy) {
    & $GradlePath "-Dorg.gradle.java.home=$JdkPath" -p $legacyProject remapJar "-PvortexResources=$resources" "-PvortexSources=$generated" --no-daemon
    if ($LASTEXITCODE -ne 0) { throw 'Production client remapping failed.' }
    & (Join-Path $PSScriptRoot 'test-legacy-production.ps1') -JdkPath $JdkPath
    $candidate = Join-Path $legacyProject 'build/libs/vortex-client-1.21.11-fabric-0.2.6.jar'
}
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
Copy-Item -LiteralPath $candidate -Destination (Join-Path $dist "vortex-client-$MinecraftVersion-fabric-0.2.6.jar") -Force
Write-Host "Verified experimental Minecraft $MinecraftVersion candidate: $candidate. Full GUI/gameplay testing is still required."
