# Local waypoints and picked blocks

## Manager and editor

Press **B** in a world to open the waypoint manager. Search by name, group or integer
coordinates; all query words must match. Sort by nearest or name. Group opens an
explicit list of groups with their visibility switches instead of cycling through names.
Click a row to edit, use Show/Hide to keep entries saved, or use + New waypoint.
The editor is a separate page with name, group, XYZ, a visible color palette and block
selection. New markers inherit the current group filter. Delete requires confirmation.
World/server and dimension isolation, death markers, and the temporary Storage Finder
marker are retained. Inventory/hotbar UI is not modified.

## Pick real blocks

In the editor choose **Select blocks in world**. The editor draft stays in memory;
the screen closes so the player can walk and aim normally.

- Aim at an actual block within normal Minecraft interaction reach, and press **H**.
- H toggles that exact block position on/off in the draft. It never breaks, places,
  interacts with, scans or loads blocks/chunks, and sends no selection packets.
- **Enter** or **B** returns to the editor with the selection. **Esc** cancels that
  selection session, leaving the prior editor draft unchanged.
- Press **Save** to persist the marker and block coordinates. Cancel never writes.
- New waypoints use the first selected block as their initial XYZ anchor; existing
  waypoints retain their XYZ. Current XYZ explicitly resets the anchor to the player.
- Pick block is configurable under Key bindings; the default H only acts during picking.

Selections are immutable when saved. The safety bound is 256 explicit block coordinates
per marker (independent of waypoint visibility distance). Show/Hide, groups and editing
retain picked coordinates. Clear changes only the draft until Save.

## In-world display

Markers and their wire-box highlights appear only in an 18-degree cone around the
camera look direction. Behind-camera and offscreen markers are not clamped to the HUD
edges: no side arrows or permanent screen-edge labels. Looking away hides them.
There is no distance cutoff, including beyond the terrain rendering distance.

Selected unit-block coordinates have colored, camera-projected wire boxes and a compact
name/distance pill. These are local saved-coordinate overlays, not server-side effects
or a claim that the original block still exists. They may show through intervening
terrain like other waypoint indicators; unknown blocks are never discovered or scanned.
Outlines have a per-frame rendering budget of 256 boxes to keep pathological collections
from flooding GUI geometry. Labels have no added distance restriction. F1 and the
Waypoints module toggle hide the normal display. The picker preview remains available
when explicitly editing, even if the normal module is off. Other screens hide markers.
The separate entity-health overlay retains full-onscreen projection, not the narrow cone.

## Persistence and migration

Storage remains %USERPROFILE%/.vortex/waypoints-v1.json (a private local file outside
portable instances). Moving a singleplayer world changes its local world identity.
File schemas 1/2 still load, with empty block lists; the new schema is 3. Before first
writing a legacy document, an exact copy is saved beside it as
waypoints-v1.json.pre-blocks.bak, without overwriting an existing backup. Corrupt or
unsupported originals are preserved. Updates reload under a shared file lock and use
atomic replacement where available; oversized data is rejected before replacement.
The 512-marker / 1 MiB storage safety ceilings are separate from visibility distance.

## Verification

WaypointsSmoke checks legacy constructors, selected-block persistence, hide preservation,
immutable toggle drafts, bounds/dedup/limits, search/group matching, rotated-camera look
cones, behind/offscreen rejection, long-distance visibility and responsive manager/editor
geometry. AtlasQoLSmoke checks old-schema migration and the exact backup. The full 26.3
Fabric suite and all version-port transformation/production-mapping checks include the
waypoint tests. They do not open a game window; actual picking/rendering still needs
an in-game visual test. No third-party wWaypoints code, assets or mod JAR are used.
