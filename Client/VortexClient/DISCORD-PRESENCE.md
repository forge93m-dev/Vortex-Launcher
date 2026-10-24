# Discord Rich Presence

Opt in through **Client settings → Discord presence → Share activity: ON**.
Discord desktop must be running and activity sharing must be enabled in Discord.
The client reports only the Minecraft version, Main menu / Singleplayer /
Multiplayer, elapsed time and the fixed `vortex` art asset. By default it does not
share a nickname, server address, world name, coordinates, or join secrets.

## Optional server invitations

While on a multiplayer server, open **Client settings → Discord presence** and
press **Enable server invitations (30 min)**. This is session-only, off by default,
and must be deliberately enabled for the current server. The address is disclosed
to Discord and invitation recipients through the join secret, not the visible
status text. Never advertise a private server unless you intend to share its address.
It is not encrypted or an identity/access token. Minecraft server access rules
and whitelists still apply. Invitation party size represents a two-person invitation,
not the actual Minecraft server's population/capacity.

Stop removes the advertised invitation (also on leaving/changing servers or turning
off presence); expiry is 30 minutes. Already delivered addresses cannot be revoked
or made secret again. The token is a bounded untrusted routing hint, not signed
proof of a friend, server ownership or authorization. Receiving UI always confirms
the actual address and exact-version ready Fabric instance before launch.

First-version receiver: Vortex Launcher must already be running with Minecraft
closed, **Share launcher activity** and **Receive server invitations** enabled.
No cold-start OS protocol handler, automatic modpack download, client-side live
server switching, singleplayer hosting, NAT traversal or Vortex Party service is
implemented by this feature. Discord can reject the subscription; the launcher
then shows invitations unavailable while keeping ordinary activity sharing.

Application ID: `1547929448165281924` (public identifier, not a token).
The application name displayed in Discord is configured in the Discord Developer
Portal. No application secret, bot token, or Discord login is required here.

The local IPC connection runs on daemon threads, not the game/render thread.
Activity changes are coalesced with a 15-second minimum interval (removing an
advertised invitation bypasses the interval). Missing Discord
is non-fatal and retried. “Connected” requires a successful activity acknowledgement.
Sharing is off by default; disabling it or exiting closes this application's IPC
session. No extra native libraries or network services are bundled.

Protocol tests (fake transport; do not change the user's Discord status):

```powershell
./Client/VortexClient/test-discord.ps1 -InstancePath 'bin/Debug/net8.0/instances/New Instance_(1)'
```

Manual checks still required on Windows and macOS: enable sharing with Discord
open, inspect the profile activity, switch between menu/world/server (allow at
least 15 seconds), disable sharing, then restart Discord and check reconnection.
The Discord application's name and user privacy preferences can affect visibility.

Windows IPC uses `\\.\pipe\discord-ipc-N` and an overlapped Win32 handle through
Minecraft's existing JNA dependency. A synchronous RandomAccessFile blocks writes
behind a pending read and must not be used. Read/write operations own separate
OVERLAPPED structures/events and stable native buffers until completion. Writes
time out after five seconds; close cancels pending I/O and the final completing
operation releases the pipe handle. No additional native binaries are bundled.

Explicit native diagnostic (handshake + JSON IPC ping only; no SET_ACTIVITY,
status changes or Discord user-data output):

```powershell
./Client/VortexClient/test-discord.ps1 -InstancePath '<existing instance>' -DesktopProbe
```

This additionally verifies a real READY, simultaneous pending read/write and PONG,
then cancellation of a pending read. It requires running Discord desktop.
