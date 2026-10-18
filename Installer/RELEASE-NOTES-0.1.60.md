# Vortex Launcher 0.1.60 — Beta update

Bundled Vortex Client: **0.1.54** for Minecraft **26.3 / Fabric**.

## This update

- Removed the experimental Visibility Test optimization: no culling module, render hooks or background visibility checks remain in the bundled client.
- Packages the current launcher and client work, including wardrobe/skin/cape collections, HUD customization, chest search, shulker previews, waypoints and other client QoL features.
- Provides verified update packages for Windows x64, Apple Silicon Macs and Intel Macs. Each ZIP has an exact SHA-256 checksum and size in `latest.json`.
- Updates replace the launcher application, not worlds, mods, saved profiles or settings.

## Updating an installed launcher

Close Minecraft, open **Settings → Check for updates → Download & update**. Startup checks also offer this release when enabled. After updating the launcher, update **Vortex Client** in each existing Fabric instance to get client 0.1.54. Updating the launcher alone does not replace a running game's mod.

Legacy launchers that cannot install verified ZIP packages require one manual update with the matching download below.

## Downloads

- Windows: `VortexLauncher-0.1.60-win-x64.zip` (portable/self-update) or `VortexLauncherSetup-0.1.60.exe` (installer).
- Apple Silicon (M1/M2/M3/M4 and later ARM Macs): `VortexLauncher-0.1.60-osx-arm64.zip`.
- Intel Macs: `VortexLauncher-0.1.60-osx-x64.zip`.

macOS beta bundles are ad-hoc signed and their extracted signatures, executable permissions and native command-line startup are checked on Mac runners. They are **not Developer ID signed or Apple notarized**. First launch may require **System Settings → Privacy & Security → Open Anyway**. Do not disable Gatekeeper. Full GUI/game behavior and in-place updates still need validation on users' Macs. See `OPEN-FIRST.txt`.

No FPS improvement or tested Sodium compatibility is claimed for this release.
