# Vortex Party — self-hosted preview (protocol 1)

This is an opt-in first version, **not a deployed public service**. No account,
hosting subscription, firewall change or external deployment is created by the
launcher. It does not start a server on users' computers automatically.

## What works

- Create a private party; share a 32-character random invitation with trusted friends.
- Up to eight members; local player names are **not verified Microsoft identities**.
- Manual Here/Come/Danger points, one per member, expiring after 60 seconds.
- In-game HUD shows sender, horizontal distance and XYZ, only on the same server
  address and dimension. These are HUD readouts, not world-space beacons.
- No global Minecraft chat, commands, server plugins or automatic position tracking.
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

With .NET 8 SDK:

```powershell
dotnet run --project Services/VortexParty
```

The default address is `http://127.0.0.1:5278` (loopback only). Put this in the
client's Right Shift → Vortex Party → Service URL. Another PC cannot connect to
your localhost. The launcher does not expose ports or change the firewall.

## Online hosting (separate deployment step)

Publish with `dotnet publish Services/VortexParty -c Release -o <output-directory>`.
The host requires the ASP.NET Core 8 runtime. Use an HTTPS reverse proxy and an
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
./Tests/PartySmoke/test-local.ps1 -InstancePath '<downloaded 26.3 Fabric instance>'
./Client/VortexClient/test-local.ps1 -InstancePath '<downloaded 26.3 Fabric instance>'
```

Tests run an isolated loopback service, connect two real Java transports, exercise
authorization/isolation/expiry/bounds, then stop their own service. Fabric smoke
checks transformation, GUI layout, opt-in defaults and server/dimension filtering;
it does not open a real Minecraft window or claim visual multiplayer validation.
