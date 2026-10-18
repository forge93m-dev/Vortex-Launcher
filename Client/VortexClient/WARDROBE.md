# Wardrobe — client 0.1.42

## Built-in cape selection fix and English UI

- Vanilla/Vortex/Aurora/Ember try-on and local Apply no longer wait for the shared cape network busy flag. Selecting a bundled design does not start a request, cancel an in-flight upload, or opt into online services. Pending uploads still refer to their captured cape; changing the local choice does not publish it accidentally.
- Cancel preview and loading saved collection previews are no longer blocked by background network lookup. Custom Apply still waits for its required image/network work. If capes are disabled, the Apply notification explicitly points to Enable rather than implying they are already visible in-world.
- All built-in client status labels, details and notification strings are English. User-entered design names and Minecraft's own localization remain unchanged. Regression smoke tests activate the actual four built-in tiles and apply each while the shared network flag is busy.

## Cape evidence and notification cards

- Cape-tab status replaces the generic Saved capes label. Hover for detail: Preview, Hidden here, Not checked, Local only, Published, Publishing, or Unconfirmed. Labels/details are English. Local saves and access keys never prove publication; only a successful upload/select acknowledgement or the existing opt-in lookup does. Evidence is per UUID, session-only, capped at eight profiles, and expires after five minutes.
- Upload requests have sequence tokens. Old/cancelled responses cannot claim success for a newer request or another profile. A failed/ambiguous attempt offers Retry for that same applied cape; retry is an explicit user action, never an automatic upload. Disabling capes here does not remove an earlier online publication. Preview and publishing stay separate.
- Original dark bottom-right notification cards show explicit save/apply/activation/publish results and errors. Success lasts 3.5 s, information 4.5 s, errors 7.5 s of visible time. At most three cards and eight queued entries; duplicates are suppressed and pending errors take priority over information when full.
- One top-level Gui hook draws cards after the HUD/screen. It leaves vanilla toasts, menus, input, inventory, crosshair and hotbar unchanged. Gameplay layout reserves the lower 64 GUI pixels and avoids enabled HUD rectangles/the crosshair; if no slot fits, cards wait. F1, loading overlays, inactive windows and the HUD editor suspend display/timers. Changing profile clears old notices.
- Sliding/fading, stack settling and a lifetime bar use the existing reduced-motion preference. Notifications have a separate ON/OFF control in client Settings. No shaders, extra images, disk/network reads or UI resource allocations are needed in the idle drawing path. Notification text is bounded plain text; raw exceptions, keys, codes, paths and request bodies are never included.
- Headless checks exercise queue caps/priority/dedup, visibility timers, reduced motion, GUI-scale/HUD collisions, publication uncertainty/retry/expiry/isolation and transformation of the actual global GUI hook. Actual GPU rendering still requires an in-game visual check.

## Try-on, collection management and HUD editor QoL

- All skin/cape tiles (including Vanilla) now change only the wardrobe preview. Apply commits the current tab locally; Cancel or leaving without Apply discards the preview. Imports are still archived in the collection, even when their try-on is cancelled. Skin arm type is part of the preview. Async selection results are rejected after cancellation/profile changes.
- Cape uploads remain separate: Online publishes the applied cape only and refuses an unapplied preview. Enable is still an explicit opt-in; local Apply never opts into network lookup. Custom selection restoration/upload now reads the content-addressed collection, with the old per-instance file as a missing-file-only fallback.
- Saved tiles have a visible three-dot menu, also accessible by right-click or R. Edit names (up to 40 code points), toggle Favorite, or confirm removal. Favorites sort first within saved items. Active applied designs must be changed before removal; bundled/Vanilla choices cannot be removed.
- Optional bounded binary `.meta` sidecars store names/favorites without modifying images. Metadata errors fall back to generated labels. Removed images and metadata are moved together to `.vortex/wardrobe/.trash/<unique-id>/` for manual recovery. No original imported PNG is deleted. Deleted thumbnails are released on refresh.
- HUD dragging now aligns to the closest screen or enabled-overlay edges/centers within five GUI pixels and shows the actual matching guide. Snap OFF keeps free dragging. Inventory/hotbar rendering remains vanilla.
- Ctrl+Z and the expanded toolbar's Undo undo placement/size/opacity/reset edits. One drag/resize is one step; no-op clicks are ignored. History is screen-local, capped at 32, and restores raw automatic-anchor/inherited-appearance values, not just calculated coordinates. Non-HUD settings are excluded.
- Headless tests cover real try-on/cancel/local-Apply boundaries and online opt-in isolation, Unicode metadata/favorites persistence, recoverable removal, nearest guides, history bounds, raw restoration and real Ctrl+Z input routing. In-game visual QA remains outstanding.

## Lightweight interface motion

- Changing the preview cape fades the old cloth out (160 ms), swaps the texture at zero opacity and fades the new cloth in (220 ms), using smoothstep opacity. No scale pulse, rotation or cloth flourish. Only one avatar/cape is submitted, avoiding overlapping transparent geometry. Repeated/rapid selections preserve the current opacity and transition toward the latest choice.
- Only detached `WardrobePreview.PreviewState` capes use the translucent pass during the transition. Body, clothing, world players and steady-state cape rendering retain their normal opacity/render type. Old/target custom textures are temporarily protected from cache pruning while the screen needs them.
- Collection tiles reveal with a bounded stagger, smoothly highlight hover/selection and compress briefly on activation. Sidebar selection slides between Skin/Cape without resetting the camera.
- Dock controls ease on hover, shared action buttons gain a growing underline, module tiles compress on activation and sliders smoothly highlight their knobs.
- Disabling UI animations removes these transitions. Animation state uses elapsed time; no image decoding, file/network requests or additional render targets are introduced by these effects.
- Headless smoke checks cover fade boundaries, interruption, settling, texture identity, addition/removal, cape-only submission and reduced motion. Actual GPU rendering still needs an in-game visual check.

The in-game Wardrobe replaces the Capes entry in the title/pause menu and Vortex hub. Skin/Cape navigation and collection controls are on the left; a standalone Minecraft AvatarRenderState preview is on the right. Drag rotates the model; Front/Back buttons reset its orientation. Preview does not equip or rotate the actual player. The cape is rendered by Minecraft's avatar cape layer, not pasted onto the model as a flat overlay.

## Saved collection and dark theme

- User-wide collection in `user.home/.vortex/wardrobe/skins` and `capes`, independent of instance and executable location. New imports preserve previous designs; checksum-addressed files deduplicate matching images and keep classic/slim variants distinct. Maximum 64 per type bounds storage and thumbnails. Corrupt entries are skipped without deleting or overwriting them.
- Previously saved current skins/capes are copied into the collection on restoration when possible. Originals and existing per-profile selections stay in place. Skins and capes that were overwritten before this feature cannot be reconstructed.
- Saved skin/cape tiles only preview on click. Apply commits locally; Online remains a separate explicit upload action. No automatic upload is introduced.
- Scrollable thumbnail tiles stay clipped to the collection area. File decoding and thumbnail construction happen off-thread, not during drawing. Thumbnail GPU textures are released when leaving the screen.
- Modules, wardrobe and shared client controls now use opaque charcoal surfaces, dark slate primary buttons and light text rather than mint/white fills. Vanilla inventory, hotbar and chat are unchanged.
- The Cape tab smoothly turns the preview to the back, zooms to 1.65x and centers the cloak; the Skin tab returns to front/full-body framing. Manual drag remains available, the camera survives widget rebuilds/resizes, and disabling UI animations snaps directly to the requested view.

## Local skins

- Select a Minecraft PNG skin, exactly 64×64, at most 1 MB. Photos, JPEG, legacy 64×32 and HD skins are not supported in this first version.
- Choose classic/slim arms. Selection remains a preview until Apply is pressed.
- Vanilla skin + Apply restores the original skin while keeping a recoverable local image.
- Skins are per game-profile UUID in `user.home/.vortex/skins/<uuid>.skin`, outside instance exports and launcher extraction directories. The original PNG is not modified.
- File reading/decoding/storage is asynchronous; textures are installed on the game thread. At most an applied and a draft texture are retained, and only the local player's skin is overridden. Elytra and cape data remain separate. Custom body textures are not marked as authenticated Mojang skins.
- The base skin layer is made opaque like vanilla; outer clothing may remain transparent.

## Network boundary

Local skin import/application makes no network requests and does not change a Mojang account. Other players do **not** see these custom skins yet. Skin upload, authenticated ownership and remote skin lookup require an extension of the cosmetic service and are not represented as available by this UI.

Existing online cape operations are preserved. Access opens the separate invitation/profile-ID page. No access tokens, invitations, Cloudflare storage or deployed service were changed by this feature.

## Verification

Fabric smoke tests cover class/mixin linkage, 320×240 and larger layout bounds, standalone avatar state, rotation bounds, strict skin PNG validation, base/outer transparency, classic/slim state, persistence, reset, corrupt-file preservation and cape/elytra separation. GPU rendering and mouse interaction in an actual game window still need a live check.
