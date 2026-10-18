# Exploration QoL

## Controls

- **B**: Waypoints. Type any group name in the Group field; use the left selector to filter by group and its Hide / Show button to hide/show a whole group. Individual marker visibility remains independent.
- Death Waypoints: captures coordinates and dimension once per death, including the health-packet path for immediate respawn, queues storage while another waypoint save is busy, and adds a marker to **Deaths**. Notifications never claim a failed save succeeded.
- Compact Shield: 50–100% first-person size, default 72%, slightly lowered. Other items, inventory/third-person rendering, server state, blocking and hitboxes are unchanged.
- Enchantment Hints: English explanations for bundled known vanilla enchantments, on items and enchanted books. Unknown mod enchantments are not guessed. Vanilla names/levels and effects remain unchanged.
- Chest Search: Ctrl+F; normal words must all match. `-gold` excludes a word. Categories: `#food #armor #weapon #tool #block #enchanted #damaged` (or `type:food`). `ench:mending` searches an enchantment name/ID, including stored book enchantments. `dur:<25%` searches remaining durability; `< <= > >= =` are supported. Combine filters, e.g. `#tool ench:efficiency -gold dur:>=50%`. Unsupported filters match nothing. Nothing is clicked, moved or sent to the server.
- Settings: **?** opens a scrollable plain-English explanation with controls, examples and limits. Hover over settings for quick help.

## Storage and verification

World map and minimap were removed in 0.1.51, including terrain sampling, map rendering, background tile workers and the M shortcut. Existing generated cache files are left untouched; this client no longer reads or writes them. Worlds, waypoints, groups and death reminders are preserved.

Waypoint location remains `%USERPROFILE%/.vortex/waypoints-v1.json`. Legacy schema 1 entries load into General; the next edit saves schema 2 so older clients reject grouped data instead of silently stripping group metadata. Update older instances' Vortex Client before editing shared markers. Unknown/corrupt originals are preserved. Hidden-group preferences are per-instance settings.

Automated checks cover actual 26.3 Fabric/mixin linkage, shield transform isolation/restoration, real enchantment tooltip callbacks, advanced filters against real ItemStacks, legacy waypoint migration and group visibility, death deduplication/packet hook, and absence of removed map classes/shortcuts. They do not replace GPU rendering, live multiplayer or long-session performance checks.
