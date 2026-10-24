# Launcher Discord activity

Enable **Settings → Appearance → Discord → Share launcher activity**, then press
**Save settings** to remember the choice. Sharing is off by default and separate
from the Minecraft client's own Discord switch. The status label distinguishes
disabled, connecting, acknowledged activity, unavailable/rejected, and game handoff.

The activity reports generic sections: library, Discover, profiles, launcher
settings, wardrobe, and instance management. It never sends instance names,
account names, file paths, search queries, or server addresses. Updates are
coalesced to at most one every 15 seconds. While a launched Minecraft process is
running, launcher IPC closes so it does not compete with the client's presence.
Closing the launcher or disabling sharing also closes its IPC session.

No Discord token, bot, OAuth login, native SDK or remote service is needed.
Windows uses asynchronous named pipes; macOS/Linux use Unix domain sockets.
All connections and frame handling run off the UI thread. Discord must be running
as a desktop app and user activity privacy settings must allow sharing.

Public application ID: `1547929448165281924`.
Upload `Assets/Branding/vortex-icon-v1.png` to that application's Rich Presence
Art Assets in Discord Developer Portal and name the asset **vortex**. The launcher
uses this fixed asset key; there is no image setting in the launcher. The same
PNG can be uploaded separately as the app icon under General Information.

## Play together preview

The generated non-AI cover `Assets/Branding/vortex-discord-cover.png` is 1024×576.
Upload it to **Cover Image** in Rich Presence Art Assets. Its editable build recipe
is `tools/build-discord-cover.ps1`, using the existing Vortex logo and typography.
Uploading this image does not itself enable joining.

The host runs the updated client on a multiplayer server and explicitly enables
server invitations for 30 minutes in **Client settings → Discord presence**.
See the client's `DISCORD-PRESENCE.md` for disclosure and limitations.

The receiver must already have Vortex Launcher and Discord desktop open, Minecraft
closed, and both **Share launcher activity** and **Receive server invitations** on.
The launcher subscribes only to ACTIVITY_JOIN. It never auto-accepts: a bounded,
rate-limited confirmation shows the server and filters installed, ready instances
to the exact Minecraft version and Fabric loader. No matching instance means Join
is disabled. Version matching does not verify mod compatibility. Normal confirmed
launch preparation may check/download missing game files; invitations themselves
never install a modpack. Minecraft 1.20+ uses `--quickPlayMultiplayer`; earlier
releases retain the legacy server/port arguments.

Wire format: `vxi1.` plus unpadded canonical base64url of ASCII
`version|address|base36-expiry-unix-seconds|16-lowercase-hex-nonce` (≤128 characters).
Parsing rejects control characters, schemes, userinfo, paths, bad ports, invalid
IPv6, non-canonical tokens, expired tokens and expiry over 31 minutes ahead. It
performs no DNS or network request. This is not signed authentication; inspect
the address before joining. No secrets are saved to config or intentionally logged.
Diagnostic export redacts invitation strings and Quick Play address arguments.

Cold launching from Discord, accepting inside running Minecraft, singleplayer
hosting and modpack synchronization remain unsupported. No Discord registration,
OS handler, cloud deployment or public release was performed. Real cross-user
Discord invitation delivery and macOS behavior must still be verified manually.
The application may need further Discord-side configuration before its Join action
is available; fake IPC tests do not establish that public behavior.

Extra tests: `Tests/SidebarSmoke --discord` checks actual modal controls without
starting Minecraft; `Tests/DiscordSmoke` includes cross-language token vectors,
expiry/validation, subscription acknowledgement/rejection, event gating and flood
bounds. `Client/VortexClient/test-discord.ps1` verifies the Java side.

Tests: `dotnet run --project Tests/DiscordSmoke/DiscordSmoke.csproj` uses a fake
fragmented transport, not the user's actual Discord. Checks privacy allowlist,
handshake, acknowledgement/error, ping/pong, coalesced section updates, game
handoff, disable, retry backoff and malformed frames. UI placement is checked by
`Tests/SidebarSmoke --alignment`. Live Discord visibility and macOS execution
still require manual verification; a successful local build is not that test.
