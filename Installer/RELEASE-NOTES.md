# Vortex Launcher 0.2.3 — Artwork startup crash hotfix

## Fixed

- Fixed a native crash while loading covers and icons that could prevent 0.2.2 from opening. The same crash was reproduced with generated mixed-format images before the fix.
- Replaced the scaled codec path with a single owned decoder over immutable image data, followed by pixel resizing and a copied bitmap. Decoding is serialized and bounded to 20 MiB of encoded data and 16 megapixels; unsupported or oversized artwork is omitted rather than blocking startup.
- Added regression checks for concurrent PNG/JPEG/WebP loads, repeated garbage collection, damaged thumbnail caches, invalid images and extremely tall/wide images. These checks run in the normal interface test suite on Windows and both Mac architectures.

Includes unchanged **Vortex Client 0.2.0**. Installation-local library behavior from [0.2.2](https://github.com/forge93m-dev/Vortex-Launcher/releases/tag/v0.2.2) is retained. This hotfix does not delete or move instances, worlds, profiles or settings.

## If 0.2.2 does not open

1. Close Minecraft and all Vortex windows before replacing application files.
2. Download the ZIP for your platform from this release and extract it separately.
3. **Windows:** replace only `Vortex Launcher.exe` in the existing launcher folder. Keep every other file and folder, especially `instances` and `config`.
4. **macOS:** replace only `Vortex Launcher.app` in the same installation folder. Keep the adjacent `Vortex Launcher Data` folder.

Do not delete the installation folder or reinstall Minecraft. A manual application-only replacement is necessary if the old launcher closes before its updater can be opened. If Vortex still opens, use **Settings → Updates → Check for updates**.

## Storage reminder

Windows libraries stay beside the executable. Mac libraries stay in `Vortex Launcher Data` beside the .app, outside its signed bundle. A new empty installation folder starts clean; old global AppData/Application Support libraries are preserved but are not automatically connected. Use the same folder to keep your current local library. A fresh local installation does not delete online Vortex accounts or separately stored client cosmetics.

## macOS beta notice

Apple Silicon and Intel packages are built on native Mac runners. Extracted bundle signature, executable permission and native command-line startup are verified. Packages are **ad-hoc signed, not Apple Developer ID signed or notarized**. First launch may require **System Settings → Privacy & Security → Open Anyway**. Do not disable Gatekeeper globally. Automated checks are not a full GUI/game test on your Mac.
