# Download regression checks

Run `dotnet run --project Tests/DownloadSmoke/DownloadSmoke.csproj -p:UseSharedCompilation=false`.

Uses a synthetic HTTP handler and generated Minecraft manifests. No Mojang requests,
game launches, user worlds or existing installations are touched. Cache fixtures go
into a unique temporary folder; installation fixtures go under the test executable.

Covers streaming SHA-1, cold/warm cache, modified files of identical size, invalid
checksums, cancellation during transfer, safe retry, concurrent cache users, bounded
24-worker scheduling, aggregate progress, native extraction and Forge's vanilla copy.

QoL checks also exercise single/batch Undo (collisions, busy guards, partial retry,
exact bytes and disabled state) and reviewed share ZIPs (portable metadata, default
privacy exclusions, opt-in categories, import preview compatibility, stale files,
overwrite refusal and cancellation cleanup).

The printed synthetic install time is a regression diagnostic, not a promise about
real internet throughput. Real installations write `.vortex-installation-timing.txt`
inside their instance folder, including planning, file preparation, extraction and
network byte counts. Original instance-local assets/libraries remain portable and
independent; files are not hard-linked across instances.
