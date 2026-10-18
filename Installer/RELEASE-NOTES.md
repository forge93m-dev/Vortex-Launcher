# Vortex Launcher 0.2.1 — Beta 2 hotfix

## Fixes

- Fixed a launcher crash when scrolling, resizing or closing a long mod-check report. Empty recycled list items are now handled safely.
- Stopped treating every optional bundled JAR as an active mod. Fabric remains responsible for choosing bundled alternatives, including mods that package variants for several Minecraft versions.
- Unsupported version predicates (including prerelease rules) are now informational, not compatibility warnings. They remain in the manual report but do not interrupt every launch.
- Kept checks for proven missing required dependencies, duplicate enabled top-level mod IDs, known incompatible versions and unreadable JAR archives.

No mods are removed, disabled or changed by this update. Offline inspection is not a complete compatibility solver; Fabric can still report genuine dependency problems.

Includes unchanged **Vortex Client 0.2.0**. All features from [Beta 2](https://github.com/forge93m-dev/Vortex-Launcher/releases/tag/v0.2.0) are retained.

## Updating

From **0.2.0**, use **Settings → Updates → Check for updates → Install update**. Keep existing instances, saves, settings and accounts.

The original **0.1.0** predates the working updater and needs one manual application-only replacement. On Windows, close the launcher and Minecraft, extract the Windows ZIP separately and replace only `Vortex Launcher.exe` in the old launcher folder. Do not delete the old folder or its data. On macOS, replace only the application bundle and keep Application Support data.

## macOS beta notice

Apple Silicon and Intel packages are built on native Mac runners. The extracted bundle signature, executable permission and native command-line startup are checked. Packages are **ad-hoc signed, not Apple Developer ID signed or notarized**. First launch may require **System Settings → Privacy & Security → Open Anyway**. Do not disable Gatekeeper globally. GUI/game behavior and in-place updates still need validation on users' Macs.
