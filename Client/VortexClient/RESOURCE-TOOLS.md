# Resource Counter and Storage Finder

Both modules are opt-in. Open Vortex > Modules, enable the module, then open its settings.

## Resource Counter

- Open **Manage resource goals**. Enter an item ID (for example `minecraft:stone`) and quantity, or choose **Use held item** while in a world.
- Save up to four goals; saving the same ID replaces its target. Remove goals individually.
- Counts inventory/hotbar and offhand items, not armor, chest contents, the cursor stack, or lifetime pickups. Putting resources in storage lowers the count.
- Goals persist in the instance's `config/vortex-client.json`. HUD Editor supports placement, scale, opacity and undo.

## Storage Finder

- Manually open a chest, barrel or shulker box. Only received menu contents linked to the recent block interaction are remembered.
- Open **Search remembered storage** in the module settings. Search item names or IDs, with all words required. Results are nearest-first and include count, XYZ, distance and last-seen age. **Copy XYZ** copies coordinates only.
- **Mark** creates one temporary, local last-seen marker using the same on-screen projection as waypoints, including an edge indicator when off-screen. It works independently of the Waypoints module and is never written to the saved waypoint list. **Clear marker**, clearing history, reopening the marked container, disabling Storage Finder or leaving the scope removes it.
- Search radius defaults to 64 blocks and can be set to 16–256 in module settings. The results list scrolls.
- These are last-seen snapshots, not a live guarantee. Another player can change contents after you leave. No closed storage, chunks or block entities are scanned.
- Ender chests, minecarts and menus without a corresponding manual storage-block interaction are excluded. A server plugin replacing a real chest's menu cannot always be distinguished from that chest's normal contents.
- History is session-only, capped at 256 containers, never uploaded or written to disk, and cleared on disconnect, world/dimension/profile change, disabling the module, or confirmed **Clear history**. Double chest halves share a canonical coordinate.

Automated checks exercise model logic, real stack/menu snapshots, live mixin transformation, persistence and layout bounds. They do not replace an in-world multiplayer/GUI test.
