# Vortex Launcher 0.2.0 — Beta 2

The first major public update after **0.1.0**. Intermediate 0.1.x development build numbers are not a history of separate public feature updates.

Includes **Vortex Client 0.2.0** for Minecraft **26.3 / Fabric**.

## Launcher

- Redesigned library, instance management, content browsing, settings and profile screens, with consistent animation and clearer controls.
- Mod, resource-pack and shader management; compatible mod-update previews and safer batch operations.
- Faster downloads with a shared verified cache, bounded parallel transfers, cancellation and recovery. Download progress is collected in one place.
- Repair instance, launch diagnostics and privacy-filtered error reports.
- Import previews, sharing instances and undo for supported content removals.
- Saved player-name editing, recent profiles, quick switching and Vortex account integration.
- Verified launcher updates for Windows x64, Apple Silicon and Intel Macs; package size/checksum checks and application-only replacement with a backup.

## Vortex Client

- Dark client menus, module settings and a HUD editor with placement guides and undo. Vanilla inventory/hotbar are preserved.
- Armor/held-item durability, low-durability warnings, configurable crosshair and potion effects; FPS, coordinates, ping, speed, clock and TPS displays.
- Wardrobe with player preview, saved skin/cape collections, try-on before applying and Vortex cosmetic synchronization.
- Chat timestamps and repeat folding, advanced chest search, shulker previews, compact shield and enchantment explanations.
- Waypoints with groups, hide/show controls and death-coordinate reminders. Entity health displays use only information available from the game/server.
- Customizable Vortex keybindings and notifications.

The experimental visibility/culling optimization has been removed. No FPS boost or tested Sodium compatibility is claimed. No map/minimap is included.

## Moving from the original 0.1.0

The original 0.1.0 predates the working updater. **It requires one manual transition**; a newer feed cannot repair code already installed in that executable.

**Windows portable installation:** close Minecraft and the launcher, download `VortexLauncher-0.2.0-win-x64.zip`, extract it to a temporary folder and replace **only `Vortex Launcher.exe`** in the existing launcher folder. Keep all other files and directories, especially instances, saves, settings and accounts. Do not delete the old launcher folder. Alternatively, use the installer and explicitly connect your existing library if installing into a different location.

**macOS:** use `osx-arm64` for Apple Silicon or `osx-x64` for Intel. Replace only the application bundle; retain Application Support data. See `OPEN-FIRST.txt`.

For builds with the working updater, restart the launcher or use **Settings → Updates → Check for updates → Install update**. Beta 2 compares newer than test build 0.1.60.

Existing Fabric instances need a separate **Update Vortex Client** action to receive the new client. Launcher replacement does not silently overwrite user mods or a running game's JAR.

## macOS beta notice

Both Mac architectures are built and ad-hoc signed on native Mac runners. The extracted bundle signature, executable permission and native command-line startup are checked. This is **not Apple Developer ID signing or notarization**. First launch may require **System Settings → Privacy & Security → Open Anyway**; never disable Gatekeeper globally. Actual GUI/game behavior and in-place updates still require validation on users' Macs.
