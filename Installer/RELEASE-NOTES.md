# Vortex Launcher 0.2.2 — Installation-local libraries

## New storage behavior

- Windows stores the library, profiles, settings, protected remembered Vortex sign-in, download/artwork caches and update staging beside `Vortex Launcher.exe`.
- macOS stores the library, profiles, settings, caches and update staging in `Vortex Launcher Data` beside the installed `Vortex Launcher.app`. User data stays outside the signed bundle so replacing the application does not overwrite it.
- A new empty installation folder starts with a separate, empty library. Global Windows `data-location.json` pointers and the old Mac Application Support library are no longer connected automatically.
- Temporary ZIP launch locations, macOS Gatekeeper translocation and non-writable installation folders show an actionable error instead of silently creating a library elsewhere.
- Settings now describe the active storage policy; the old global Connect existing library action has been removed.

## Important when updating

**Old data is preserved, not deleted or moved, but may no longer appear automatically.** This release intentionally changes which library the launcher opens. Windows installations that already stored instances beside the executable continue using those instances. If your previous library was elsewhere, keep its original folder and copy only the data you deliberately want to reuse after closing Minecraft and Vortex. Never overwrite a populated new library without a backup. Old global remembered Vortex sign-in is not imported; sign in again if needed. This does not delete your online Vortex account.

For macOS, previous releases used `~/Library/Application Support/VortexLauncher`. That directory is left untouched. To keep its data, copy the desired contents to the adjacent `Vortex Launcher Data` folder while the application and Minecraft are closed. Keep the original until you verify the result.

For future updates, replace only the executable/application in the **same installation folder**. Keep `instances`, `config`, caches and the adjacent Mac Data folder.

## Testing from scratch

Close Vortex and Minecraft. Install/extract into a **new, empty, permanent, writable folder**; do not copy old data into it. On Windows, run the extracted executable or use the installer with a new destination. On macOS, for example:

```text
~/Applications/Vortex Clean Test/
    Vortex Launcher.app
    Vortex Launcher Data/  (created on first launch)
```

Do not run directly from a ZIP, read-only disk image, temporary extraction, or a protected system folder. This is a local reset, not deletion of cloud accounts or cosmetics stored separately by Vortex Client.

Includes unchanged **Vortex Client 0.2.0** and all [0.2.1 launch fixes](https://github.com/forge93m-dev/Vortex-Launcher/releases/tag/v0.2.1).

## macOS beta notice

Apple Silicon and Intel packages are built on native Mac runners. The extracted bundle signature, executable permission and native command-line startup are checked. Packages are **ad-hoc signed, not Apple Developer ID signed or notarized**. First launch may require **System Settings → Privacy & Security → Open Anyway**. Do not disable Gatekeeper globally. These automated checks are not a full GUI/game test on your Mac.
