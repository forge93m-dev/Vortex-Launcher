# Vortex Party — self-hosted preview (protocol 2)

This is an opt-in first version, **not a deployed public service**. No account,
hosting subscription, firewall change or external deployment is created by the
launcher. It does not start a server on users' computers automatically.

## What works

- Create a private party; share a 32-character random invitation with trusted friends.
- Up to eight members; local player names are **not verified Microsoft identities**.
- Manual Here/Come/Danger points, one per member, expiring after 60 seconds.
- In game they appear as pins in the world (like waypoints) plus a HUD line with sender,
  horizontal distance and XYZ, only on the same server address and dimension.
- Party chat: a chat line that starts with `@p ` (or `@party `) goes only to this service,
  never to the Minecraft server. Lines are plain text (1–256 characters, no line breaks or
  bidi overrides), at most one every 0.4 s per member. The party keeps the last 50 lines in
  memory for five minutes; there is no history on disk.
- Shared waypoints: a member shares a saved waypoint (name, XYZ, colour, up to 32 blocks).
  Up to 32 per party; the author or the owner removes them; they go away when the author
  leaves. Members can save a private copy into their own waypoints.
- Ping: the party marker key marks the block you look at (up to 96 blocks) for 30 seconds.
- No public Minecraft chat, commands, server plugins or automatic position tracking.
- Leaving removes the member's marker. The owner closing the group removes everyone.
- Each member has a separate 256-bit session token; invitation codes cannot read
  snapshots. Tokens and coordinates stay in memory only, with no database/request logs.
- A missed heartbeat expires a member after two minutes. Owner expiry closes the
  group. All groups expire after six hours; restarting the service clears them.

The owner must keep Minecraft open and connected to this service. A lost invite
cannot be revoked individually yet: close the party and create another one.
Anyone who has an invite may join; this preview has no accounts, kick/ban, invite
rotation, moderation or verified friend list. Do not share invitations publicly.

## Local verification

With .NET 10 SDK:

```powershell
dotnet run --project Services/VortexParty
```

The default address is `http://127.0.0.1:5278` (loopback only). Put this in the
client's Right Shift → Vortex Party → Service URL. Another PC cannot connect to
your localhost. The launcher does not expose ports or change the firewall.

## Online hosting (separate deployment step)

Publish with `dotnet publish Services/VortexParty -c Release -o <output-directory>`.
The host requires the ASP.NET Core 10 runtime. Use an HTTPS reverse proxy and an
explicitly chosen domain, forwarding to the loopback service. Clients reject
non-loopback HTTP, URL credentials, query strings, fragments and path prefixes.
Never disable certificate validation. Redirects are not followed with credentials.

Request bodies are limited to 4 KiB. Groups are bounded to 256, members to eight,
markers to one per member, and markers to one every two seconds. The simple rate
limiter allows 300 requests/minute per immediate peer IP, with a bounded table.
Behind a proxy that IP is the proxy: deployment must configure upstream limiting
appropriately. Do not blindly trust forwarded headers. This is a small preview
service, not a production security/scaling review. There is no CORS browser API.

The service sees names, manually sent coordinates, dimensions, a SHA-256 hash of
the normalized server address, and network peer addresses while handling requests.
The server hash is a grouping key, **not anonymity**; common addresses are guessable.
No retention beyond the in-memory session/marker TTL is implemented. TLS protects
transit; this is not end-to-end encryption from the service operator.

Aliases/ports resolving to the same Minecraft host may produce different keys;
players should use exactly the same server address. Different dimensions are never
converted. Current points are text HUD readouts; automatic server joining,
modpack synchronization and launch invitations are future steps, not implemented.

## Tests

```powershell
./Tests/PartySmoke/test-local.ps1 -InstancePath '<downloaded 26.3 Fabric instance>' -ClientJar '<vortex-client jar to test>'
./Client/VortexClient/test-local.ps1 -InstancePath '<downloaded 26.3 Fabric instance>'
```

Tests run an isolated loopback service, connect two real Java transports, exercise
authorization/isolation/expiry/bounds, then stop their own service. Fabric smoke
checks transformation, GUI layout, opt-in defaults and server/dimension filtering;
it does not open a real Minecraft window or claim visual multiplayer validation.
