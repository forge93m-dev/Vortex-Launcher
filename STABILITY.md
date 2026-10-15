# Launcher 0.1.3: stability and responsiveness

The bundled Vortex Client is unchanged (0.1.21). No release or public update
manifest is published by these changes.

## Persistent data

Settings, player profiles, instance metadata and operation journals are written
to a unique temporary file, flushed and replaced with a last-valid `.bak`.
Invalid primary documents are kept in `.corrupt-<id>` before recovery; they
never replace a valid backup. If both metadata copies are unreadable, the
instance is displayed as **Unknown / Metadata needs recovery** with launch and
file mutations blocked. No Minecraft version or loader is guessed.

This protects against incomplete application writes, not a failed disk or every
possible filesystem/power-loss scenario. Back up important worlds separately.

## Startup and library

The window constructor no longer loads the library/settings. Opening the window
starts one shared background initialization task; update checks follow it.
The greeting no longer forces a 1.2-second wait. The library has a loading state
and populates its filtered view in a batch. Settings bindings do not read disk.

Cards use an ItemsRepeater with a virtualized uniform grid. Cover art/icons load
off the UI thread, share in-flight requests, decode to thumbnails and release
leases when detached. Idle decoded images have a 16 MiB / 64-entry cache budget;
visible pinned images are retained until their controls detach. Disk thumbnails
are periodically trimmed toward 48 MiB. Failed images leave a placeholder.

Repeatable headless fixture (Windows, same session, 500 synthetic instances):

| Measurement | Before | First optimized run |
| --- | ---: | ---: |
| Window construction and first layout | 2811 ms | 496 ms |
| Realized cards | 500 | 13 |
| Process working set | 406.7 MB | 105.7 MB |

This is a synthetic UI measurement, not a claim that Minecraft downloads or
game startup become 5x faster. Measurements vary with hardware and caches.

## Interrupted downloads and operations

Large files (at least 1 MiB) can retain resumable prefixes after network failure,
process termination or cancellation. Resume requires an expected SHA-1 or strong
ETag, matching saved request metadata and valid Content-Range. A server returning
200 to Range restarts the download instead of appending. Changed ETags, bad ranges
or failed checksums restart safely. Expected file hashes and archive checks still
run before publication. A process-level cache lock avoids concurrent writers.

Install/repair/import/copy operations record their source and target in the
instance directory. After restart, the library offers **Resume**, **Cancel
(keep files)** and **Folder**. Nothing resumes automatically. Cancellation does
not remove the directory, worlds, mods or settings. Cancelled operations remain
visible and may be resumed explicitly. Old tasks without a recorded source
offer folder inspection rather than guessing how to resume.

Imports require the same archive fingerprint; copies require matching source
file metadata. Already completed files are byte-verified and skipped; changed
existing files cause a safe stop, not an overwrite. Partial copy/extraction files
are separate from final game files. Corrupt instance metadata must be recovered
before an interrupted install/repair can resume.

## Verification

```powershell
dotnet run --project Tests/DownloadSmoke/DownloadSmoke.csproj
dotnet run --project Tests/SidebarSmoke/SidebarSmoke.csproj
dotnet run --project Tests/SidebarSmoke/SidebarSmoke.csproj -- --benchmark
```

All tests use generated fixtures, not the user's game library or credentials.
The verification workflow runs on Windows x64, macOS ARM64 and macOS Intel,
checks storage/recovery/resume scenarios, headless UI/input/images/virtualization,
and the native packaged executable's CLI startup. macOS packages have ad-hoc
beta signatures and are verified again after ZIP extraction. This is not
notarization, an interactive desktop GUI test or a real Minecraft play session.
