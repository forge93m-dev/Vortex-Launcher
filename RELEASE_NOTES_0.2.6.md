# Vortex Launcher 0.2.6 — NeoForge, pack import, per-instance settings and safer downloads

## New

- **NeoForge.** Create NeoForge instances (Minecraft 1.20.2 and newer). The installer comes only from NeoForged's
  official Maven and is checked against its published SHA-1. Modrinth `.mrpack`, CurseForge and PolyMC packs that
  need NeoForge now import. Not yet tested with a live game launch: please report problems.
- **CurseForge pack import.** Packs download through the CurseForge API with your own free key
  (Settings → Java → CurseForge). Files whose authors block other launchers are listed in
  `CurseForge manual downloads.txt` in the instance folder.
- **Java, memory and JVM arguments per instance** (instance Settings tab). Imported packs never bring their own.
- **New Game hub design**, Nunito font, smoother motion and faster installs.
- **Vortex Client**: new interface, party chat and shared waypoints, better chat, player list, accent colours,
  smooth menus, animated capes, zoom and a panorama picker.

## Platform

- The launcher now runs on .NET 10 (LTS). .NET 8 support ends on 10 November 2026.
  CI workflows use the .NET 10 SDK. Publish scripts are unchanged.
- Avalonia stays on 11.0.6 in this release. A trial upgrade to 11.3.22 built cleanly,
  but headless motion tests showed transitions skipping ahead after idle periods. That
  needs a visual check on real Windows/macOS first, so it is planned separately.

## Security and reliability

- **Signed update feed.** `latest.json` now carries an ECDSA P-256 signature over the version,
  notes and every package's URL, size and SHA-256. The launcher offers an update only when the
  signature matches the release key embedded in the app (`vortex-2026-10b`). A modified feed —
  even one with valid checksums for a different file — is refused. The release scripts sign
  the feed (`tools/UpdateSigner`) and refuse to publish an unsigned or untrusted one. This needs
  the `VORTEX_UPDATE_SIGNING_KEY` repository secret.

- Java: automatic Temurin downloads now check the SHA-256 and size published by the
  Adoptium API. A tampered or truncated archive is discarded. A JRE is used when
  available (about half the size of a JDK). Existing runtimes are kept.
- Forge: the installer JAR (which is executed) comes from the official Forge Maven first.
  A mirror copy is accepted only when it matches the official SHA-1. Installer entries
  can no longer write outside the instance `libraries` folder. Each install uses its own
  temporary file name, so parallel installs do not overwrite each other.
- Launch arguments: the custom JVM arguments field respects quotes
  (`-Dpath="C:/My Folder"`). Paths containing `#`, spaces or quotes are quoted
  correctly in the Java argument file.
- Asset index: the index named by the installed version metadata is used, not
  whichever index file is listed first.
- Create instance: Forge and Quilt availability are checked independently. Switching
  versions quickly no longer shows stale results.
- Modrinth requests send the launcher's real version in the User-Agent. Shared HTTP
  clients are configured once (fixes a rare race).

## Cleanup

- Removed unused code: the legacy `InstallForgeOrNeoForgeAsync` path and `ModCompatibilityService`.

## Tests

- Update feed signature: 8 new checks (unsigned, redirected URL, replaced checksum, altered notes,
  untrusted key, unknown key id, production key isolation, valid feed).
- New `HardeningChecks` in DownloadSmoke: 24 checks covering the fixes above.
  Synthetic fixtures only, no network access.
- Results with .NET 10:
  - DownloadSmoke: 415 PASS
  - SidebarSmoke: 926 PASS
  - DiscordSmoke: OK
- Win-x64 and osx-arm64 single-file publishing works.
- Not verified yet: launching real Minecraft with Forge or NeoForge on Windows and macOS.
