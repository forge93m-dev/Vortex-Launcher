# Vortex Launcher 0.2.9 — new windows, Vortex Client 0.2.5

## Launcher

- **New instance window.** Loaders are tiles (Vanilla, Fabric, Forge, NeoForge, Quilt) with a short description and a hint,
  the newest versions are one-click chips, and a live preview sits on the left.
- **Import review.** Importing a pack now shows its name, format, versions, counts of mods, resource packs, shader packs and
  worlds, what the launcher fetches itself, a warning about files that need a manual download, and the disk space needed.
  An import is blocked when the CurseForge key is missing or the pack does not fit on the disk.
- **Instance window.** Sections are a sidebar with counts, and the overview shows cards for launch settings (memory, Java,
  arguments), mods and the most recently played worlds.
- **Appearance settings.** Eight accent colours (the same as Vortex Client), a new light palette, Comfortable or Compact list
  density, and a live preview.
- Fixed an unreliable test of CurseForge imports (no change for players).

## Vortex Client 0.2.5

- **Animated custom capes.** Choose a GIF or a PNG strip (frames stacked top to bottom, each twice as wide as tall) with
  "Animated..." in the Wardrobe. HD frames up to 256 wide, at most 32 frames, shown to other Vortex Client players.
  Older clients simply do not see them.
- **Event Alerts.** A short sound and a notice when a friend joins or leaves your party, shares a waypoint or ping, writes your
  name in chat, or when your health or hunger gets low. Every event can be switched off.
- **Your skin in chat.** Mods that read skins from the tab list (chat heads) now show your Vortex skin instead of the default one.
- Everything from 0.2.4: animated capes Nebula, Flow and Pulse are shared online.
- Existing instances: use **Update Vortex Client**. Worlds, mods and settings are preserved.

## Notes

- Only Minecraft 26.3 is the main target; 26.2, 26.1.2, 26.1.1, 26.1 and 1.21.11 stay experimental.
- Checked by automated tests; the new client features were not played through on every Minecraft version.
- Animated custom capes use the legacy activation-key path; accounts do not support them yet.
