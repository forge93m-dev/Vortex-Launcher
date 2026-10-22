# Shared Minecraft files

Minecraft assets are now stored once in `game-files/assets` under this installation's data folder. Vanilla, Fabric and Quilt libraries use `game-files/libraries`. On Windows the data folder is beside the executable; on macOS it is the existing `Vortex Launcher Data` folder beside the application, never inside the signed bundle.

Each instance has a `.vortex-shared-game.json` manifest containing relative paths to its exact libraries and asset index. Launching never adds every JAR in the shared directory to the classpath. Index filenames include a digest of their source identity, so different Minecraft releases can safely use the same upstream index ID. Library identities include their URL and expected SHA-1 when supplied.

Forge keeps its libraries and generated processor outputs inside the instance for compatibility with its official installer. It still shares assets. Natives, client JARs, worlds, mods and settings remain private. Existing local files are not removed. An instance without shared metadata retains its legacy launch layout; successful preparation creates the new layout atomically.

The existing download cache can populate the shared store without another download when it already has valid files. Subsequent instances reference the shared files directly, avoiding thousands of repeated copies. Integrity checks remain enabled. Repair invalidates remembered verification for both the instance and shared storage, and restores damaged assets/libraries from verified cache entries or downloads. Rebuilding metadata preserves damaged originals through the existing safe JSON writer.

Asset verification is also remembered across launcher restarts in `game-files/.verified-assets.json`. A matching content-addressed filename, expected size, saved size, modification time and creation time allow an unchanged asset to skip another full content read. New or changed files still go through SHA-1 verification. This is a performance cache, not a tamper-proof attestation against an attacker preserving file timestamps. Repair resets it and performs full checks. The persistent shortcut never applies to executable libraries, mods, Java or launcher updates. Corrupt/missing cache metadata causes full checks; stale backups are not trusted. The installation timing report records how many assets reused this shortcut.

Asset path planning checks each hash-prefix folder once per batch, while still rejecting linked asset files. This avoids thousands of repeated ancestor checks and directory creation calls. Full installation stage timing is recorded separately in `.vortex-installation-stages.txt`, including version lookup, Minecraft/Fabric preparation, Java selection, loader setup, Vortex Client setup and finalization. Interrupted work is marked incomplete.

Do not delete `game-files` as disposable cache: installed instances reference it. Move it with the launcher data folder. No PolyMC files or accounts are imported or changed automatically.

## Verification

`Tests/DownloadSmoke/SharedGameFilesChecks.cs` covers cross-instance reuse without game-file requests/copies, exact classpath selection, loader replacement and dependency inspection, colliding upstream index IDs, Forge's private library layout, cancellation, corrupt metadata recovery, path traversal rejection, legacy fallback and relocation. The full download smoke suite also covers checksum repair, native extraction and preservation of worlds/mods.

These are synthetic automated tests, not a real macOS game launch or an internet-speed benchmark. A first installation still has to transfer missing files; no fixed download time is promised.
