# Launcher 0.1.4

## Mod management

Manage an instance → Mods → **Manage mods** opens a virtualized installed-mod
list with search, All/Enabled/Disabled/Selected filters, selection and bulk
enable/disable/remove. **Page** identifies the exact file by hash before opening
its Modrinth page; unknown files do not open a guessed search result.
Removal requires approval and keeps files in
`.vortex-trash/mods`. Toggle names are `.jar` / `.jar.disabled`; old `.disabled`
files remain supported. Existing target files are never overwritten by toggles.
File changes are refused while the instance is running/busy.

## Update preview

**Preview mod updates**, or Updates → Check updates, identifies installed files
by SHA-512 via Modrinth. Filename/title guessing is no longer used. Only newer
compatible Minecraft/loader releases are offered; beta/alpha require opt-in.
The preview shows current/new versions, target filename, size, date and a plain
text changelog. Select individual updates, then explicitly approve the batch.
Checking/closing/cancelling the preview does not replace mods.

Unrecognized files and the bundled Vortex Client are not replaced. Network
failures are reported separately from no available updates. Required/exact and
incompatible dependencies in recognized enabled mods are checked against the
selected resulting versions. Missing/disabled/conflicting dependencies block
the update with an explanation; install/enable them or change the selection.
This is not a guarantee against every runtime incompatibility.

All selected files download to an instance-local staging directory before any
replacement. Size/archive/SHA-512 checks and a fresh inventory/hash check precede
commit. Originals and a path/hash manifest remain in `.vortex-mod-updates`.
Handled failures/cancellation roll back an in-progress batch without overwriting
external edits. A hard process/power interruption between different-file renames
can require manual restoration from this manifest/backups; no automatic
crash-recovery/undo UI is claimed for this new update path.

## Local diagnostic report

**Save error report** is available in the game console, launch-error dialog and
instance-manager header. A save picker creates a local ZIP; nothing is uploaded.
Only summary metadata (Minecraft/loader/version), mod filenames, a bounded
console tail, latest.log and the newest crash-report text are eligible. Raw
accounts/settings/worlds/options/server lists/screenshots/mod archives are not.
Common credential fields, JWT/bearer strings, IDs, emails, usernames, local home
paths, IPs and chat/connection lines are redacted. Text tails are bounded at
2 MiB; oversized/truncated individual lines are omitted. Redaction is
best-effort; arbitrary third-party mod logs may contain other private data.
Review the ZIP before sharing it. Existing destination files are not overwritten.

## Icon and verification

An original transparent V/vortex icon was generated with the built-in image
tool. The preserved PNG master and generation prompt are in `Assets/Branding`.
ICO includes 16–256 px sizes; ICNS includes 512/1024 px. Windows executable,
main-window branding/icon and macOS bundle packaging use these versioned assets.

Tests use offline generated fixtures: update filtering/selection/dependencies,
bad checksums, stale files, rollback, cancellation, collisions, privacy checks,
UI commands, selection/filter states and minimum-window action layout. The
existing three-platform verification workflow also runs these regressions.
No public release/auto-update manifest is published by this change.

API references: [file-hash identification](https://docs.modrinth.com/api/operations/versionfromhash/),
[version metadata](https://docs.modrinth.com/api/operations/getprojectversions/).
