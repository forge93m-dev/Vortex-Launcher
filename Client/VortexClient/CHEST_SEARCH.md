# Chest Search — client 0.1.47 / launcher 0.1.52

Enabled by default; toggle Chest Search under Modules > Gameplay. Works only on
ChestMenu storage (chests, double chests, barrels and ender chests) and shulkers.
The open storage's contents are searched, never unopened containers, inventory,
hotbar, armor or crafting slots. No packets, item movement or server automation.

Click Search or press Ctrl+F; match localized/custom names and registry IDs, literal
case-insensitive words (all words must match). Matches get green borders; unrelated
occupied slots dim. The count reports matching/occupied storage slots. Escape clears
and unfocuses; Enter unfocuses. Typing E, Q or numbers cannot trigger vanilla item
actions while search is focused. Clicking slots releases search focus.

Query is per open screen, capped at 64 characters, retained across resizing but
not across different containers. Matching is cached per draw pass and refreshed
at container tick/query change. Compact GUI scales place the field in the title
row rather than over slots. Only the small search control is added; vanilla art,
item tooltips and transfers remain unmodified. No inventory-wide UI redesign.

Verified through actual Fabric transformation and headless real-container tests:
localized/literal multi-word queries, registry IDs, 27/54-slot storage, per-tick
cache invalidation, unchanged item references/counts, player-slot exclusion,
bounded scaled layouts and focused E/Q/number input capture. Native IME focus
and rendered in-world appearance still need a live game check.
