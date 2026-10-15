# Vortex Launcher for macOS

## Build and share

The **Build macOS beta (no Apple account)** workflow builds and ad-hoc signs
the app on native Apple Silicon and Intel runners. It needs no Apple certificate
or paid Apple membership. Pushes to `codex/macos-beta` start it; it creates only
Actions artifacts, never a GitHub Release or a new update manifest.

Download `VortexLauncher-beta-osx-arm64` for Apple Silicon or
`VortexLauncher-beta-osx-x64` for Intel. Inside the artifact is the actual
`VortexLauncher-<version>-<architecture>.zip`, first-launch instructions and a
verification report. Send that inner ZIP without re-zipping the application.
The builder verifies the extracted app signature, executable permission and
native CLI startup. This does not replace testing the GUI/game on a user's Mac.

On a Mac, `./publish-macos.ps1 -Runtime osx-arm64` (or `osx-x64`) also produces
the signed ZIP in `release`. Windows cross-builds in `publish/osx-*` are
**unsigned diagnostics**, not the signed beta: do not send them for installation.

Extract the signed ZIP on the Mac and drag **Vortex Launcher.app** to **Applications**.
The package contains .NET, so your friend does not need to install the .NET SDK.
The bundle currently targets macOS 12 or later.

These are development/test packages: they have not been tested on a real Mac
and are not Developer ID signed or notarized. macOS may block the first launch.
Only open a build you trust, using the per-app **Open Anyway** approval in
**System Settings → Privacy & Security**, if offered. Do not disable Gatekeeper
globally. For normal public distribution, sign and notarize the application.

## Data and Minecraft

Instances, accounts, settings and Java runtimes live in
`~/Library/Application Support/VortexLauncher`, outside the application bundle.
Replacing or moving the app does not move/delete your worlds or settings.
Windows keeps its existing portable layout.

Java is selected/downloaded for the game version and native-library architecture.
Modern versions with ARM natives use ARM Java on Apple Silicon. Older versions
without ARM natives use Intel Java and require Rosetta 2. macOS may offer to
install Rosetta when starting an Intel binary. Some old mods/native libraries
may still need additional compatibility work; test the exact version/modpack.

Vanilla, Fabric, Quilt and Forge use the same existing launch flows. NeoForge
remains unsupported; this port does not change loader availability.

## Signing and notarization

On a Mac, after extracting the package, a developer with a Developer ID
Application certificate can sign the bundle with hardened runtime and the
`Installer/macOS/Entitlements.plist` file. Sign nested Mach-O binaries before
the enclosing bundle when adding external native libraries.

For this single-file package:

```bash
codesign --force --options runtime --timestamp \
  --entitlements Installer/macOS/Entitlements.plist \
  --sign 'Developer ID Application: YOUR NAME (TEAMID)' 'Vortex Launcher.app'
codesign --verify --strict --verbose=2 'Vortex Launcher.app'
ditto -c -k --keepParent 'Vortex Launcher.app' VortexLauncher-signed.zip
xcrun notarytool submit VortexLauncher-signed.zip --keychain-profile YOUR_PROFILE --wait
xcrun stapler staple 'Vortex Launcher.app'
```

For notarized public distribution, validate the downloaded Java runtime and all
embedded native binaries on the target Mac as well. Apple's signing tools and
a developer certificate are not available in the Windows build environment.

## Updates

Use `latest.multiplatform.example.json` as a template. Publish the Windows
installer and both Mac ZIPs in a new GitHub release, then update `latest.json`
with the real version and links. A Windows-only `.exe` link is not offered to
Mac users. Version 0.1.2 introduces verified ZIP self-updates and automatic
replacement of the current app bundle with a backup. The helper waits for the old
launcher to close and does not replace Application Support data or bypass Gatekeeper.
The manifest must include SHA-256, size and the mac-app-zip package kind.
Old/debug builds need one manual replacement. See RELEASE.md and the manually
triggered prepare-release workflow; nothing is published automatically.

References: [Avalonia macOS deployment](https://docs.avaloniaui.net/docs/deployment/macos),
[Adoptium API](https://github.com/adoptium/api.adoptium.net/blob/main/docs/cookbook.adoc).
