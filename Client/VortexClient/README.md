# Vortex Client — first playable foundation

## 0.1.26: invitation-based capes

Right Shift > CAPES offers Vortex, Aurora and Ember bundled cape designs plus Vanilla.
Selection is a local preview; explicitly enable capes to display them and query the
shared service. Join the target world/server, copy its actual profile ID, obtain a
beta invitation from the administrator, Activate, then Save online. Other enabled
Vortex clients refresh nearby profiles about once per minute. Disabling capes hides
them locally; save Vanilla online first to remove your Vortex cape for others.

Cape texture replacement preserves the body skin and vanilla rendering checks.
Gliding gear bypasses replacement so elytra keep original textures. No external
texture URLs are accepted. The client never sends chat, locations or server addresses.
Private keys live outside game directories in `~/.vortex/cape-keys.json`; do not share
that file. API calls run off the game thread with size/time limits and no redirects.
No real player has been enrolled automatically. Automated Fabric/transport tests pass;
actual two-player rendering remains to be checked in-game.

## 0.1.25: graphics tools removed

The Performance screen, low-end preset and ambient particle limiter are removed.
No particle-engine mixin remains. Users with an existing saved preset backup see
only a Restore graphics from removed preset button in Settings; it retains later
manual changes. No graphics setting is changed automatically. Other modules remain.

An optional client-only mod for **Minecraft 26.3 + Fabric Loader 0.19.5 or newer + Java 25**.
It has no Fabric API dependency and contains no game libraries or platform-specific native code.
The same JAR is used on Windows and macOS, including Apple Silicon.

## Current modules

- **Vortex Party preview (0.1.23)**: Right Shift → Vortex Party. Create/join an
  invite-only group using an explicitly configured service URL. **No public service
  is deployed yet.** Use G (rebindable) for Here, or Party buttons for Come/Danger.
  Only manual points are sent; no automatic player location feed or Minecraft chat
  messages. Points expire after 60 seconds. The HUD shows sender, XYZ and horizontal
  distance on the same server/dimension only. F1 hides them. Groups are session-only,
  max eight; credentials stay in memory. HTTP is allowed only for loopback tests;
  online hosting needs HTTPS. Networking is off the game thread with one in-flight
  request, bounded 64 KiB responses, timeouts, stale-marker hiding and retry backoff.
  Local display names are not verified accounts. This is not a world-space beacon,
  friend-account system, modpack invitation or automatic server joining feature.
  Deployment/privacy details: [Party service](../../Services/VortexParty/README.md).

- **Chat**: local `[HH:mm]` timestamps and a `×N` counter for consecutive identical
  messages received within 60 seconds. Each option and the module can be disabled
  separately. Equality includes text styles, hover/click metadata, source and tags.
  Only display lines are folded; original messages and signatures remain in vanilla
  history for reporting/deletion. No chat packets or log contents are rewritten.
  History remains bounded by Minecraft's normal limit. Arrival times are session-local.
- **Smooth UI chrome**: original antialiased outline icons replace pixel-drawn module
  symbols; search/settings share the same icon style. Rounded controls use a shared
  pre-rendered alpha-mask atlas (maximum seven quads per shape), keeping soft edges
  without per-pixel geometry. Eight module cards need 296 shape quads versus about 7152
  in 0.1.18. The main-menu gradient is a single quad. UI glyph sources are resolved once
  per font/resource generation; fallback and F3+T resource reload remain supported.
  These are geometry/cache regression tests, not a measured in-game FPS claim.
  Icon PNGs are generated offline from original
  vector paths by `tools/GenerateIcons.java`; no AWT is used inside Minecraft.
  `tools/GenerateCorners.java` regenerates the antialiased corner atlas.
- **Fullbright**: brighter caves/nights through the client lightmap only. Does not save
  gamma, add Night Vision effects, send packets or alter mob spawning/light levels.
  Darkness/Blindness remain respected. Disabled by default; turn off for vanilla lighting.
- **Block Info**: name, registry ID, coordinates and item icon of the block under the
  crosshair. Uses Minecraft's existing visible-block hit result, never scans through walls
  or requests hidden block information. Background-free and movable/scalable in HUD Editor.
  Disabled by default; previews appear only in the editor when no block is targeted.

- **Main menu and pause menu**: floating original Vortex emblem, Minecraft's standard panorama,
  compact dark primary actions and a bottom icon dock with English tooltips and smooth hover/press feedback.
  No store, cosmetics or friend buttons are invented. Vanilla utility actions delegate to their original buttons.
  The main menu draws its panorama once; the transparent in-game hub and module/settings screens do not blur the world.
  Right Shift opens a small Vortex hub over the actual HUD: Mods in the center, HUD Editor/settings beside it,
  and Modules/HUD/Settings/Keybinds/Back in the bottom dock. The dock remains keyboard-accessible.
  Inventory, hotbar and existing HUD placement/background choices are not restyled.
  No large central panel. Animated hover accents and
  English labels. The pause menu keeps the world visible. Buttons delegate
  to their original vanilla handlers, including disabled multiplayer checks and
  save/disconnect behavior. Both menus include a Vortex Client shortcut. Inventory,
  hotbar, chat, Options and other Minecraft screens are not restyled.

- **FPS Counter**: HUD overlay; respects F1; adjustable size and opacity.
- **Ping / Speed / Clock / TPS**: optional, independently movable HUD overlays, disabled
  by default. Ping uses the current player's server-provided latency; singleplayer says
  Local. Speed measures horizontal blocks per second, ignoring teleports and vertical falls.
  Clock uses the computer's local time in 24-hour format. TPS is explicitly approximate,
  derived passively from server time packets over five intervals; unknown/stale data shows
  a dash. Changing world/server resets the estimate. No extra packets or commands are sent.
- **Modern local UI fonts**: bundled Manrope regular/semibold, oversampled TrueType glyphs,
  dark navy surfaces and lavender accents. The title menu uses a quiet gradient instead
  of the Minecraft panorama. Pause keeps the world visible. Only Vortex menus/editor use
  the custom font: vanilla inventory, hotbar, chat and Options are unchanged. The bundled
  font's SIL OFL license is included under `assets/vortex_client/font/OFL-Manrope.txt`.
  A required, namespaced client resource pack registers the assets with Minecraft even
  without Fabric API. Missing/unavailable UI fonts fall back to readable vanilla text.
- **Zoom**: hold **C**, then scroll up to zoom in or down to zoom out (2x–8x).
  Fractional trackpad scrolling is supported. The hotbar does not change while zooming.
  C is the default; the hold key can be changed in Settings > Keybinds or Zoom settings.
  The chosen strength is saved when you release the hold key; no settings menu is required.
- **Freelook**: enable the module, then hold **V** to orbit the player in third person
  without changing the player's view/aim or movement direction. Release to restore
  the previous first/front/back perspective. Settings allow any keyboard key and 25–200%
  sensitivity, layered over vanilla mouse sensitivity/inversion/cinematic smoothing.
  Existing camera collision remains vanilla. Closing focus, opening a screen, death,
  sleep, changing world/player or changing perspective interrupts freelook; an F5 choice
  is preserved. Disabled by default and unavailable while spectating another entity.
  Follow server rules: some servers prohibit independent perspective mods.
- **Module menu**: press **Right Shift** while playing or from the main/pause menu, then select **MODS**.
  Rounded module cards, animated switches and hover states, search, and All/HUD/Gameplay/Enabled
  categories. The grid scrolls continuously using wheel/trackpad, a draggable scrollbar
  or Page Up/Down/Home/End. Only visible cards render, clipped cards cannot capture clicks
  outside the viewport. Search/category changes reset the scroll. Click a card to toggle it;
  right-click, click its settings icon, or focus it and press R to open individual settings.
  Size, opacity and zoom strength use continuous mouse/keyboard sliders. Settings persist
  per instance in `config/vortex-client.json`.
- **Free keyboard bindings**: Settings > Keybinds captures a key for the Vortex menu,
  Zoom or Freelook. Escape cancels; Delete/Backspace unbinds; Reset defaults restores
  Right Shift / C / V. Duplicate Vortex keys are rejected and vanilla-control overlaps
  show a warning without changing Minecraft controls. Main/pause menu shortcuts stay
  available even if the menu binding is cleared. Assignments persist per instance.
- **Low durability alerts**: Armor HUD > Durability alerts controls the threshold
  (1–30%, default 10%) and enable switch. Low armor/held-item numbers turn red, and a
  five-second background-free notice appears while playing. Each low item is reported
  once with a 30-second global cooldown; repairing above the threshold re-arms it.
  Checks are tick-based, not render-frame based. F1/focus loss/open screens defer new
  notices instead of consuming unseen alerts. No chat messages or sounds are added.
- **Keystrokes removed**: legacy configurations cannot re-enable the old overlay.
- **Held items integrated into Armor HUD**: no separate module or movable overlay.
  Damageable main-hand/offhand items follow the armor icons, using the same position,
  orientation, size and opacity. Armor HUD settings include **Items: ON/OFF** and
  **Off: ON/OFF**. Durability shows the remaining amount, with no background.
- **UI animations**: eased menu entrance, lifting module cards and press feedback
  in Vortex settings, the main menu and pause menu. Disable via Settings > UI animations.
  Inventory, hotbar and other vanilla interfaces remain unchanged.
- **Crosshair**: optional replacement for the vanilla crosshair sprite. Cross, dot and
  circle shapes; six color presets; size, gap, thickness, opacity, outline and center-dot
  controls. A settings preview is shown where screen space permits. Vanilla first-person,
  spectator/debug checks and the attack cooldown indicator are preserved. Disabled by default.
- **Potion Effects**: active effect icons, names/levels and countdowns (including infinite
  duration). Timers turn red below ten seconds. Move/resize in HUD Editor; disabling this
  module restores vanilla effect icons. Equipment and effects have editor-only samples.
- **Coordinates**: green XYZ labels, white block coordinates, an eight-way compass direction
  and an English biome name, without a background. Enabled by default in the top-left corner.
  Negative positions use Minecraft's block rounding, not truncation. Modded biome IDs are
  formatted as readable names; longer names fit the available GUI width.
- **Armor HUD**: equipped armor icons, remaining durability only and thin green/yellow/red bars,
  without a panel background. Initially anchored eight GUI pixels from the bottom/right edges;
  resizing the window or changing GUI scale keeps it in that corner until manually moved.
  Empty slots have no labels; items without durability have no placeholder text.
  The editor shows sample armor only when no armor is equipped, never in normal gameplay.
  In Armor HUD settings, **Layout: Vertical / Horizontal** switches orientation. Horizontal
  armor runs left to right: helmet, chestplate, leggings, boots, with durability below each item.
  Each horizontal slot fits its own icon/number, with only a two-GUI-pixel gap between slots;
  long modded durability numbers do not force oversized padding around every other item.
  The setting is saved per instance and applies to the HUD Editor bounds as well.
- **Toggle Sprint**: tap your Minecraft sprint key to latch/unlatch sprint. Vanilla hunger,
  movement and focus rules still apply; this is not automatic sprint or a speed boost.
  Disabled by default. Does not modify the saved Minecraft accessibility setting.
- **HUD Editor**: open from the module menu, drag enabled overlays, then click Done or
  press Escape to save. In a world, the real Minecraft HUD (hotbar, health, hunger and XP)
  stays visible without menu blur or full-screen shading. Controls start collapsed in the
  top-right corner; click Controls or press H to show/hide the top panel, leaving the hotbar free.
  Outside a world, enter one to preview the real HUD. F1 visibility is respected, not changed.
  Selected overlays have corner markers and alignment guides.
  Positions snap near screen edges and the center; the grid and snapping can be switched off.
  Drag the highlighted bottom-right handle or scroll to resize the selected module.
  Arrow keys move it one pixel; Shift + arrows move ten pixels. Right-click an overlay to
  open its settings. Size/Opacity buttons adjust its individual appearance; Reset restores positions.
  Global defaults are in Settings; individual editor overrides take precedence.
  Reset restores coordinates top-left and armor bottom-right. Upgrades move only untouched
  old factory-default positions; customized coordinates are preserved. FPS defaults
  are spaced away from the new coordinate readout.

Menu, Zoom and Freelook keys are configurable. They do not trigger while typing in chat or interacting
with other screens. Zoom changes only camera projection, never the player's saved FOV option.

The broader Feather-equivalent feature set is incremental, **not complete**. Keystrokes,
Mousestrokes and CPS are excluded by user preference. Waypoints
are **not implemented yet**. Future local phases include additional camera controls, waypoint/block
information, visual settings and additional QoL modules. Voice chat, cross-server chat
and protected server hosting require a separate network service and are not promised by
this standalone mod. The module menu does not pretend
that these modules are available. Some servers restrict client modifications such as Zoom;
follow the server's rules.

## Launcher integration

Select Minecraft 26.3 and Fabric in **Create Instance**, then explicitly enable **Vortex Client**.
The launcher installs its bundled JAR before marking the instance ready. It does not download
additional libraries, replace other mods or execute commands from a remote manifest.
Incompatible configurations are disabled. Different or disabled copies of Vortex Client result
in an actionable error, not a silent overwrite.

For an existing instance you can add the JAR from `dist` through **Manage > Mods > Add file**.
Disabling the JAR in Mods disables the entire client. The launcher does not silently re-enable
or reinstall it on each launch; the individual modules can also be turned off in-game.

## Build

Normal development: install JDK 25 and Gradle 9.6.0, then run `gradle stageClient`.
This project uses the non-remapping Fabric Loom plugin because Minecraft 26.3 is unobfuscated.

On Windows, an alternative offline build uses an already downloaded official 26.3 Fabric instance:

```powershell
./build-local.ps1 -InstancePath 'C:/path/to/26.3-instance' -JdkPath 'C:/Program Files/Java/jdk-25.0.2'
```

The script reads that instance's game/libraries but never modifies it. Only Vortex sources and
metadata are packaged into `dist/vortex-client-26.3-fabric-0.1.22.jar`.
Rebuild this JAR before rebuilding/publishing the launcher after changing the Java sources.
The launcher embeds the JAR, including in self-contained Mac builds.

This is an early test build, not a claim of compatibility with every modpack.

Run `./test-local.ps1 -InstancePath 'C:/path/to/26.3-instance'` after building.
It verifies all five Fabric mixins, Zoom math, sprint isolation, menu geometry and filtering,
compass sectors, biome labels, durability bars, corner anchoring and old-layout migration,
config round-tripping, real editor click/drag/release and corner-resize methods with SDL button codes,
keyboard nudging, and per-module settings in a temporary instance. It does not launch a game window or validate
interactive rendering. In-game testing of dragging, item icons and sprint is still required.
