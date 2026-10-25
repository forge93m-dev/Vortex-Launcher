# Vortex Launcher: notes for Claude

Minecraft launcher (Vanilla, Fabric, Forge, Quilt) for Windows, plus macOS test builds.
Built with C#, .NET 10 and Avalonia 11.0.6 (MVVM via CommunityToolkit.Mvvm). The bundled
Fabric client mod "Vortex Client" lives in `Client/VortexClient`. GitHub: `forge93m-dev/Vortex-Launcher`.
The owner speaks Russian: reply in Russian.

## Layout
- `Core/Launch`: game download/install (`GameDownloader`), launch arguments (`GameLauncher`),
  Java (`JavaResolver`, `JavaRuntimeManager`), shared assets/libraries, resumable downloads.
- `Core/Network`: Mojang, Fabric, Quilt, Forge, Modrinth services; launcher updates
  (`UpdateService`, `LauncherUpdateInstaller`, `LauncherUpdateManifest.cs`); Vortex Client install.
- `Core/Management`: instances, settings, accounts (offline nicknames only), atomic JSON (`SafeJsonStore`).
- `Core/Diagnostics`: crash analysis, offline mod preflight checks, diagnostic reports.
- `ViewModels`, `Views`, `Styles`: Avalonia UI. `MainWindowViewModel` is large and split into partial files.
- `Services/`: separate server projects (VortexParty, VortexCosmetics), excluded from the app build.
- `tools/UpdateSigner`: signs and verifies `latest.json`. Excluded from the app build.
- `Tests/`: console smoke-test programs (not xUnit). They print `PASS:` lines and exit non-zero on failure.

## Build and test
```
dotnet build "Vortex Launcher.csproj"
dotnet run --project Tests/DownloadSmoke/DownloadSmoke.csproj   # ~337 PASS: storage, downloads, updates, hardening
dotnet run --project Tests/SidebarSmoke/SidebarSmoke.csproj     # ~838 PASS: headless UI
dotnet run --project Tests/DiscordSmoke/DiscordSmoke.csproj
./publish-win-x64.ps1        # single-file exe + release/VortexLauncher-<ver>-win-x64.zip
./publish-macos.ps1 -Runtime osx-arm64
```
Run both smoke suites after every change. UI motion tests are timing-sensitive.

## Release flow (GitHub Actions, manual)
1. Bump `<Version>` in `Vortex Launcher.csproj` and add `RELEASE_NOTES_<ver>.md`.
2. Run **Prepare launcher release packages (manual)**. It builds Win/macOS, then
   `Installer/GenerateUpdateManifest.ps1` writes `release/latest.json` and signs it with
   `tools/UpdateSigner`.
3. Run **Publish verified launcher release (manual)** with that run ID. `PublishPreparedRelease.ps1`
   verifies the signature and the checksums, then publishes and updates `latest.json` on `main`.

## Update feed signature (added in 0.2.6)
- `latest.json` carries `keyId` and `signature`: ECDSA P-256, SHA-256, IEEE P1363, Base64.
  The signature covers version, notes, and each package's kind, size, SHA-256 and URL
  (`UpdateManifestSignature.BuildPayload`).
- The trusted public key is embedded in `UpdateManifestSignature.ProductionKeys`
  (key id `vortex-2026-10b`). The private key exists only in the GitHub secret
  `VORTEX_UPDATE_SIGNING_KEY` and in the owner's offline backup. Never ask for it, never print it,
  never commit it (`*.key` is gitignored).
- Rotating keys: first ship a release that trusts both the old and the new key, then sign with the new one.
- Launchers only offer an update when the feed is signed. An unsigned feed shows "not signed by the publisher".

## Done in 0.2.6 (unreleased as of 2026-10-24)
- Moved .NET 8 to .NET 10, because .NET 8 support ends on 2026-11-10. Updated CI to `10.0.x`.
- Java downloads from Adoptium are verified by SHA-256 and size; the JRE is preferred over the JDK.
- Forge installer: comes from the official Maven first. A mirror copy is accepted only if it
  matches the official `.sha1`. Paths inside the installer cannot escape `libraries`
  (`ForgeService.ResolveInside`). Temp file names are unique.
- JVM arguments field: quote-aware splitting (`GameLauncher.SplitUserArguments`); argfile quoting
  handles `#`, spaces and quotes. The asset index is read from version metadata.
- Forge and Quilt availability are checked independently. Modrinth User-Agent carries the real version.
- Removed dead code (`InstallForgeOrNeoForgeAsync`, `ModCompatibilityService`).

## Known issues and decisions
- Avalonia 11.3.22 was tried and reverted: in headless tests, transitions jump ahead after
  idle periods (`NavigationMotionChecks` "press surface" check). Before upgrading, verify on real
  Windows. This would also remove the vulnerable `Tmds.DBus.Protocol 0.15.0`, which is Linux-only.
- `DragEventArgs.Data` is obsolete in newer Avalonia; it will need `DataTransfer`.
- Microsoft account login is shelved: it was built (device-code flow, DPAPI-sealed refresh token, tested against a fake
  Microsoft) in commit 1a21665 on 2026-10-25 and removed again at the owner's request in the commit that reverts it.
  Restore it with `git revert` of that revert. It needs the owner's own Azure client ID and Mojang's approval of that app.
  Until the owner asks again, keep offline profiles only.

## Backlog (owner's choice of order)
1. ~~NeoForge support~~ done: `ForgeService.InstallNeoForgeProfileAsync` (official NeoForged Maven only, SHA-1 checked, no mirror),
   `GameLauncher` treats NeoForge as part of the Forge family (`ForgeService.IsForgeFamily`). The installer and a real launch
   were NOT run live (needs a download); verify 1.21.1 first, and the 26.x line (its main class is unconfirmed).
2. ~~Import Modrinth `.mrpack` and CurseForge packs~~ Done: `.mrpack` was already supported; CurseForge `manifest.json` packs
   import through the CurseForge API with the user's own key (Settings → Java → CurseForge). Files whose authors block other
   launchers are listed in `CurseForge manual downloads.txt`. NeoForge packs are imported like the others.
3. ~~Java, RAM and JVM settings per instance~~ Done (`Core/Launch/LaunchSettings.cs`, panel on the instance Settings tab).
4. Avalonia upgrade (see above).
5. Split `MainWindowViewModel`; move the tests to xUnit.

## Conventions
- Keep the existing safety style: atomic writes, checksum verification, cancellation,
  busy-instance guards, and never delete user worlds or mods.
- Preserve each file's line endings and BOM (the code mixes CRLF and LF).
- Add regression checks to the matching smoke suite for every fix.
