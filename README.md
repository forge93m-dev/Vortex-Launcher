# Vortex Launcher

Minecraft launcher for Windows, with macOS test builds, supporting Vanilla, Fabric, Forge and Quilt.

See [MACOS.md](MACOS.md) for Apple Silicon/Intel packages, installation and testing limitations.

Release files are published through GitHub Releases.

## Vortex Capes in 0.1.29 / Client 0.1.26

Right Shift > CAPES opens three original bundled cape designs and Vanilla fallback,
flat previews, a separate explicit opt-in and Save online. Nothing connects until
enabled. The shared service is deployed at the origin recorded in
`Services/VortexCosmetics/DEPLOYMENT.md`. An invitation bound to the actual in-game
UUID is required to publish a selection; choosing a preview alone is local only.
Offline nicknames are not treated as proof of account ownership.

The client modifies only the player cape texture using ordinary vanilla rendering,
cape model-part visibility and animation. Original skins are retained; players with
gliding gear keep their entire vanilla skin to avoid changing elytra textures.
Unknown designs, disabled clients and absent profiles use vanilla capes. No arbitrary
texture URLs, game-server plugin or skin/account token is required.

Nearby loaded player UUIDs are batched (max 32) about once per minute in a background
worker. No requests run in skin rendering, and patched skin records are cached.
Old shared results expire after five minutes without success; toggling off or changing
worlds invalidates pending results. Requests are bounded, timed out and do not follow
redirects. Activation/ownership keys are stored outside all instances under the user's
home `.vortex/cape-keys.json`, never in exported packs or diagnostics.

Automated tests cover real Fabric skin transformation, texture UV resources, skin
isolation/cache, screen bounds, private atomic/concurrent key saves and real local HTTP
requests, malformed/oversized responses and redirects. The deployed API and local SQLite
security tests pass, but a live two-player visual check is still needed.

## Removed graphics tools in 0.1.28 / Client 0.1.25

The 0.1.24 graphics preset, Performance screen and ambient particle limiter have
been removed. New builds do not modify graphics settings or inject into particle
creation. If an instance previously applied the preset and has a saved backup,
Settings shows only Restore graphics from removed preset. This restore-only path
keeps subsequent manual graphics changes; nothing is restored automatically.
Fabric tests verify the removed classes are absent from the packaged JAR and that
legacy backup restoration/serialization remains available. Update existing instances
with the launcher's Update Vortex Client action. Installation speed improvements remain.

## Lower-overhead Minecraft installation in 0.1.26

Large file sets (128+ files) use up to 64 parallel transfers; small batches retain
24, and loader libraries keep their separate 8-transfer limit. The cache HTTP
client permits 64 connections per server. Transfers reuse pooled working buffers
and cache-to-instance/vanilla-JAR copies no longer allocate two large per-stream
buffers for each tiny asset. Small files use 64 KiB working buffers, large files
256 KiB. SHA-1 verification, atomic destination publication, cancellation, resumable
downloads, cross-process cache locks and separate writable game-file copies remain.
No hardlinks connect instance files to the shared cache.

`DownloadSmoke --installation-speed` compares the previous copy implementation's
managed allocations with pooled copies, and 24 versus 64 transfers over 256 local
4 KiB fixtures with an injected 20 ms response delay. A development measurement
was about 35 MiB versus 1 MiB allocated for 128 copies, and 361 versus 247 ms for
the queued downloads. These are synthetic measurements, not an internet download
speed guarantee; results depend on the disk, network and download server.

## Installation and startup reliability in 0.1.25

Disk-full failures are distinguished from RAM problems and Minecraft crashes, including
nested Windows IO exceptions regardless of the system language. Downloads stop rather
than retrying writes to a full disk; existing verified files and resumable prefixes are
retained. Windows performs early per-file free-space checks for new cache downloads,
destination copies and extracted native binaries (not a whole-install reservation;
actual write failures are still handled). Report-export errors suggest another drive
or copying the details without creating a ZIP.

Game preparation verifies assets and libraries even when their sizes match, repairing
corrupt files from the shared cache where possible. Incomplete client metadata and
damaged native archives cannot produce a successful installation. Native extraction
uses only the current manifest's platform archives, starts after successful downloads,
and is no longer repeated by startup by scanning obsolete JARs. Quilt startup uses
the Quilt fallback Maven repository consistently with installation. Worlds, user mods
and settings are not removed by these changes. Embedded Client remains 0.1.23.

Regression checks use synthetic local HTTP fixtures, not real Minecraft instances:
disk-full/no-retry/recovery, same-size corruption, stale natives, invalid native ZIPs,
failed downloads preserving existing natives and incomplete version metadata.

## Compact content controls in 0.1.24

Mods, resource packs and shaders keep their main add action, search and content list.
Rare tools live in a labeled/accessible ellipsis menu; mod rows retain their enable
switch and one actions menu instead of separate Page/Remove buttons. Mod bulk
actions appear only after a checkbox is selected and disappear when selection clears.
Mod checks, updates, importing local files, refresh, folders and backups are retained
in More. Update preview has an explicit return to installed mods. The standalone
mod manager uses the same action menu definitions. Content catalog selection reset
and filter reset also move into More, leaving installation as the main footer action.
Removal confirmation, undo, busy/running-instance guards, search state, row
virtualization and existing interface animations remain unchanged. No user content
is modified by this UI update. Client version remains 0.1.23.

## Offline mod checks in 0.1.23

Play now inspects local enabled JARs before network preparation and again before
launch. The embedded Mods page and workspace tools expose **Check mods** too.
An individual report names files, explains errors/warnings, supports copying and
opening the mods folder, and never changes or downloads mods automatically.
Known blocking errors have no launch-anyway button; unverified findings remain warnings.

Fabric v1 metadata supports duplicate top-level IDs, required dependency presence,
stable numeric version rules, author-declared `breaks`/`conflicts`, aliases, server-only
environments and declared nested JARs. Nested alternatives are not duplicate top-level
mods. Disabled JARs are excluded. The selected local Fabric Loader supplies its declared bundled libraries,
so MixinExtras is not falsely treated as a missing top-level download.
Unknown archives, snapshots, custom overrides, unresolved aliases and inspection
limits never become guessed hard mismatches.
Java/unpinned loader versions remain startup checks; Forge/Quilt dependency graphs
and full Fabric SAT resolution are not implemented. No network lookup is on this
preflight path, and there is no guarantee against mixin/runtime crashes.

Reading is bounded by JAR/metadata/nested-entry/count budgets, is cancellable and
does not extract archives. Launch preparation locks conflicting launcher mod writes.
The range subset follows the [Fabric metadata specification](https://docs.fabricmc.net/develop/loader/fabric-mod-json)
and [comparison operators](https://github.com/FabricMC/fabric-loader/blob/master/src/main/java/net/fabricmc/loader/api/metadata/version/VersionComparisonOperator.java).
Regression checks: `dotnet run --project Tests/DownloadSmoke -- --mod-check` and
`dotnet run --project Tests/SidebarSmoke -- --mod-check` (synthetic fixtures only).
Vortex Client remains 0.1.23; Party still needs separately hosted HTTPS infrastructure.

## Vortex Party preview in 0.1.22

Bundles Vortex Client 0.1.23 with an opt-in Party screen, private invitations and
manual Here/Come/Danger points. Recently used profiles and existing UI are retained.
Use the instance's Vortex Client update action to install the new bundled client.
No public Party backend is deployed: online use requires a separately hosted HTTPS
service. Local end-to-end transport tests and source are included in
[Services/VortexParty](Services/VortexParty/README.md). No user game instances are
modified by the build/tests, and no server or firewall is started/changed on launch.

## Player names in 0.1.21

The top profile button opens a quick switcher without leaving the current screen.
Manage saved names in Profiles: rename, confirm removal, or cancel either action.
Recently selected/launched profiles sort first and retain order after restart.
Legacy profiles keep their UUIDs until renamed; a new offline name changes player
identity, not world files. The final profile cannot be removed. Failed saves leave
the previous list/selection intact. No Microsoft authentication or skin service is added.

## Continuous rounded borders in 0.1.20

Clip window contents to the inner rounded contour so opaque child backgrounds
cannot cover the curved outer stroke. Keep the native Windows corner clipping.
Maximize/restore updates both contours without adding padding or changing layout.

## Windows corner clipping in 0.1.19

Apply the native HWND region in every normal Windows window, including renderers
that report transparency support but still display opaque black corner patches.
Maximized/fullscreen windows remain rectangular. Resize/DPI behavior is unchanged.

## Windows corner fallback in 0.1.18

Custom windows keep rounded outer corners on Windows even when the compositor
reports no per-pixel transparency. An HWND region clips the real client surface,
including configurations with an invisible resize frame. Resize/DPI changes refresh
the shape; maximize/fullscreen removes it. Other platforms retain their existing
transparent/square fallback behavior. No acrylic or full-window opacity is added.

## Catalog layout fixes in 0.1.17

Catalog details now take a responsive share of the available width (320–540 px),
with a draggable divider. Mod/resource-pack/shader icons are shown in both results
and selected details, with a fallback while loading. Workspace Play text is centered
vertically and horizontally without changing the navigation/press animations.

## Unified instance workspace in 0.1.16

The library now has a compact cover and direct shortcuts to Mods, Resource packs,
Worlds and Settings & tools. Open instance stays inside the main launcher; use
Library to go back. Reopening the same instance remembers its last section.

Installed mods, bulk enable/disable, update previews, removal/Undo, local JAR import
and drag/drop share one Mods tab. Add mods opens an embedded catalog, as do resource
packs and shaders. Review compatible versions and install without another browser
window. Cached catalog selections and mod search survive section changes. Active
file operations show status/cancellation and prevent conflicting navigation/launch.

Worlds, screenshots, logs, options.txt and saved servers remain accessible. Maintenance,
client updates, compatibility checks, pack sharing and error reports are in Settings.
Confirmations and OS file pickers remain dialogs; unrelated launcher settings and
accounts are unchanged. Existing game data is not migrated or deleted.

## QoL additions in 0.1.15

- **Undo removal** in instance content/mod managers restores the last removed batch
  of mods, resource packs or shaders. Undo history lasts for the current launcher
  session (up to 20 batches per instance). Backups remain in `.vortex-trash` after
  closing. Existing names and enabled/disabled counterparts are never overwritten.
  Running/busy instances cannot be changed. World/instance deletion is not included.
- **Share pack…** from the instance menu or manager creates a local ZIP compatible
  with Import pack. Choose categories, build a preview, then save a new ZIP outside
  the instance folder. Mods/resource packs/shaders are selected by default; configs,
  worlds and screenshots are opt-in and can contain private data. Launcher accounts,
  local paths, server lists, logs, backups and caches are excluded. Nothing is uploaded.
  Changed preview files must be reviewed again. Sharing progress/cancellation appears
  in the download center.
- Bundled **Vortex Client 0.1.22** adds Chat (local timestamps and consecutive repeat
  folding, independently toggleable). Use Update Vortex Client for existing instances;
  no HUD profiles, CPS/keystrokes or screenshot actions were added.

