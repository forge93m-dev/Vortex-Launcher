# Version-specific Vortex Client builds

The launcher selects an exact embedded artifact for Fabric instances on:

- Minecraft 26.3 (existing client)
- Minecraft 26.2
- Minecraft 26.1, 26.1.1 and 26.1.2
- Minecraft 1.21.11

Other versions, snapshots, Forge and NeoForge are not assumed compatible. Minecraft 1.21.10 and 1.21.9 are the next planned ports, not supported targets yet.

## Status and verification

The additional targets are experimental ports. Verified automated checks include compilation against their own game/dependency versions, Fabric transformation of every declared mixin target, client initialization, module-menu layout, zoom math, and saved skin/cape collection behavior. The 1.21.11 artifact is additionally tested after Loom remapping with the official obfuscated game JAR, intermediary mappings and production Fabric mode. This is not a complete GUI/gameplay, multiplayer, or macOS runtime test.

Launcher 0.2.3 supports only 26.3. The additional exact-version artifacts are included in the 0.2.4 release candidate with client version 0.2.1; their experimental status remains visible in Create Instance. Build scripts do not alter live user instances.

## Architecture

- Shared implementation remains in `src/main/java` for 26.3.
- `build-target.ps1` generates isolated target sources under `build/targets/<version>`; the shared 26.3 sources are never rewritten.
- Explicit changed implementations live under `ports/26.2`, `ports/26.1` and `ports/1.21.11`. Version-specific source substitutions cover renamed game APIs and descriptors; mixin verification fails closed if they stop matching.
- Game and dependency downloads are SHA-1 checked against their official metadata. They are compilation/runtime inputs only and are not bundled in the client JAR.
- Fabric Loader 0.19.5+ is required. The 26.x builds target Java 25; 1.21.11 targets Java 21 and uses a remapped intermediary artifact, never the named development candidate.
- Each successful target build stages only its own verified mod JAR in `dist`. Existing user mods and game worlds are never installation/test destinations.

## Build

Original 26.3 build and existing tests:

```powershell
./build-local.ps1 -InstancePath '<downloaded 26.3 Fabric instance>' -JdkPath '<JDK 25>'
./test-local.ps1 -InstancePath '<downloaded 26.3 Fabric instance>' -JdkPath '<JDK 25>'
```

Other 26.x targets:

```powershell
./build-target.ps1 -MinecraftVersion 26.2 -JdkPath '<JDK 25>'
./build-target.ps1 -MinecraftVersion 26.1.2 -JdkPath '<JDK 25>'
./build-target.ps1 -MinecraftVersion 26.1.1 -JdkPath '<JDK 25>'
./build-target.ps1 -MinecraftVersion 26.1 -JdkPath '<JDK 25>'
```

Pass `-ExistingLibraries` to reuse matching, verified dependency JARs from an existing development fixture instead of downloading them again. This reads those files without changing that instance.

The 1.21.11 build requires Gradle 9.7.1 and Loom 1.17.21:

```powershell
./build-target.ps1 -MinecraftVersion 1.21.11 -JdkPath '<JDK 25>' -GradlePath '<Gradle 9.7.1 executable>'
```

Build scripts currently target the Windows development environment; the resulting Java JARs are platform-independent, but file dialogs/rendering still need testing on each supported OS.

## Port-specific behavior

- Pre-26.3 file selection uses the bundled GLFW/tinyfd API instead of unavailable SDL dialogs.
- 1.21.11 fullbright modifies current light uniforms only, not saved gamma settings or server effects; darkness/blindness keep precedence.
- 1.21.11 has no newer message-source field. Repeat folding is limited to unsigned messages rather than guessing the source of signed player messages. Original signed content/signatures remain untouched.
- World labels on 1.21.11 use the current game's FOV/projection and camera rotation through a read-only accessor.

Before public release, test a world and wardrobe on every target, especially HUD placement, waypoint projection while sprinting/zooming, container search, cape transitions, native image selection, and light changes when toggling fullbright.
