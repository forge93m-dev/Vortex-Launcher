# Vortex Launcher 0.2.5 — Compact Library & Faster Installs

## Launcher

- Replace the large Showcase banner and horizontal strip with a compact, responsive desktop grid. Browse vertically with the mouse wheel or a visible scrollbar. Play and Manage are available on each tile; search, favorites and a compact selected-instance toolbar remain accessible.
- Virtualize rows instead of creating every artwork control. Tests cover 500 instances, resizing, keyboard navigation, direct Play, filtering and recycled selection.
- Share Minecraft assets across instances, and share vanilla/Fabric/Quilt libraries using exact per-instance manifests. Forge keeps installer-compatible private libraries and generated outputs while sharing assets. Existing worlds, mods and local instance files are not deleted.
- Remember verified, unchanged content-addressed assets across restarts. Modified files require checksum verification; Repair resets the shortcut. Executable libraries, mods, Java and launcher-update packages do not use this persistent shortcut.
- Avoid repeated asset-directory checks and copies. Stage approved mods, updates and imported content with bounded parallel downloads; retain verification, rollback, cancellation and busy-instance guards. First-time downloads still depend on network and disk speed.
- Make Install/Update Vortex Client available directly in the library and instance Overview. HUD settings and other mods are preserved. Full installation stage timing helps diagnose delays outside Minecraft downloads.

## Vortex Client 0.2.2

- Resource Counter: up to four item goals with live inventory/offhand counts and an editable HUD. Off by default.
- Storage Finder: search a bounded, session-only history of containers you manually opened nearby. Results show last-seen counts, coordinates and age. Copy coordinates or place one temporary marker. This does not scan unopened containers or claim live contents.
- Clearer labeled main/pause/home controls, compact settings and module UI. Repeated active-tab clicks no longer restart transitions; module hover no longer lifts cards outside their viewport. Inventory and hotbar remain unchanged.
- Exact Fabric builds for 26.3, 26.2, 26.1.2, 26.1.1, 26.1 and 1.21.11. Ports remain experimental; 1.21.10/1.21.9 and Forge client builds are not included. Experimental renderer/visibility optimization remains disabled.
- Existing instances: after updating the launcher, select **Update Vortex Client** to install client 0.2.2. Launcher and client updates are separate.

## Windows and macOS

- Windows ZIP and installer; native Apple Silicon and Intel Mac ZIPs.
- Installation-local library locations and update preservation remain unchanged. Keep the launcher/application and its adjacent data folder together.
- Mac packages are ad-hoc signed, not Apple-notarized. Gatekeeper may require **Open Anyway**. Microphone permission support is retained; real-device voice recording still needs user testing.
- Online cosmetics still require compatible Vortex clients and successful server synchronization; this release does not promise visibility to vanilla players.
