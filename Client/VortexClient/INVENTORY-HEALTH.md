# Shulker Preview and Entity Hearts

Both modules are enabled by default and can be toggled independently in Modules.

## Shulker Preview

Hover over any vanilla shulker box in an inventory tooltip. The regular tooltip gains a 9 × 3 item grid, preserving slot order, counts and item decorations. Empty shulkers show an empty grid; unresolved server-side loot is marked **Contents unavailable**, never invented. Existing tooltip images from other mods are not replaced.

Only item data already available to the client is read. No inventory clicks, opening packets, file operations or content requests are sent. Unchanged container data reuses a read-only copied snapshot. Inventory/hotbar layout is unchanged.

## Entity Hearts

Visible mobs show compact hearts and current / maximum HP above their heads. Large health pools use a bounded ten-heart proportional row plus the exact numeric value. Players and mobs can be toggled separately, with an 8–64 m range (default 32 m).

Other players' generic client-side health can be a placeholder. Their HP is therefore shown only when the server publishes a HEARTS-rendered score in the below-name or player-list display slot. If a server withholds this data, the overlay omits that player's health; it does not guess full health. Server plugins can control the meaning and accuracy of published scores. Absorption is not included in the displayed base HP.

Only alive, visible, nonspectating players and mobs are eligible, not armor stands or the local player. Visibility and health snapshots refresh five times per second; nearest 64 candidates bound raycasts. Position projection is interpolated per frame. Hidden HUD, screens, offscreen positions and stale world snapshots suppress drawing. Nothing is written to scoreboard/entity state and no server plugin is installed.

Automated Fabric checks exercise actual item-tooltip hooks and the native tooltip factory, all shulker colors, live content replacement, defensive copying, disabled behavior, unknown-loot handling, server-score availability and nonmutation, fractional hearts and bounded boss rows. They do not exercise a live multiplayer server or GPU output.
