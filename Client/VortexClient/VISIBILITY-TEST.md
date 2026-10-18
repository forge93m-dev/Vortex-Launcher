# Visibility Test — client 0.1.53

Historical experiment only. Removed from client **0.1.54** and launcher **0.1.60** at the user's request; this document is retained as a record, not instructions for the current release.

Experimental and **OFF by default**. This is a render-submission optimization, not a graphics preset, a replacement terrain renderer, or a measured FPS improvement.

## Scope and safety

Only exact vanilla chest, trapped chest, ender chest and shulker-box block-entity/renderer classes are eligible. Barrels and creature rendering are not changed. Named storage, breaking overlays and global rendering bypass the test. Spectators, loading overlays, custom resource packs, and detected shader/portal/other culling mods bypass it too. Sodium is not blocked, but actual compatibility and additive benefit still require testing.

A currently loaded, completely opaque **rectangular wall slab** must cover the perspective projection of an expanded 3×3×3 target bound. A separating axis-aligned plane through the middle of a block layer is selected during bounded traversal toward the target. All eight corners determine conservative projection extrema (the plane must separate the eye from the entire target). Every cube in the enclosing rectangle is checked, including cells between corner rays and both sides of integer seams. This is not an eight-ray heuristic: a hole in the middle of the wall fails open. Irregular or diagonal walls may be missed deliberately.

Near-camera walls are now eligible; the previous two-block exclusion is removed. Looking out from inside an opaque block is bypassed. Positive cache entries recalculate the projected footprint and validate its opaque cells before every skip, never reusing a hidden boolean. A fixed-size, allocation-free direct-mapped opacity cache lasts only for the current prepared frame. Block destruction is therefore rechecked on the next rendered frame, without waiting for the negative-result retry timer. The shared opacity cache is bounded at 1,024 entries; collisions cause a reread, never an assumed solid block. Missing chunks are never loaded by the test. World changes and toggles clear target cache entries. Errors fail open until OFF/ON.

Limits: 1,024 cached target positions, 256 eligible checks and two new probes per prepared frame, 192 traversal steps per probe and 64 cells per wall rectangle. Checks use a cooperative 0.25 ms cumulative CPU budget, not a hard real-time deadline; a check may overshoot it. Expired proofs fail open. Status counters measure checks only, not the entire renderer or FPS savings. Eligibility, frame preparation and compatibility checks are outside that counter. Hidden counts are repeated skipped render submissions, not the number of unique chests.

## Manual validation and comparison

1. Exit Minecraft normally, update Vortex Client through launcher 0.1.59, then launch. Open Modules → Gameplay → Visibility Test. Leave it OFF initially.
2. Use one fixed view toward storage behind an opaque stone wall, after chunks finish loading. Keep window focused, graphics settings, camera, scene and FPS caps identical. Compare OFF / ON / OFF for at least 30 seconds each. Reset counters before each interval; record status as well as FPS/frame times.
3. Move around wall edges, open lids and double chests, break the blocking wall, replace stone with glass/slabs, rename a chest, enter spectator, change dimensions, disconnect/reconnect, and test at different GUI scales. Visible models must appear immediately; breaking overlays must remain visible. Disable the module if anything pops incorrectly.
4. Repeat separately with Sodium alone plus Vortex, using matched scenes. Do not infer compatibility merely from the mod-ID allowlist. Custom packs or conflicting culling/shader mods should display a bypass status rather than run the optimization.

No benefit is promised in open landscapes, scenes without eligible hidden storage, capped/background rendering, or workloads dominated elsewhere. Additional CPU checks can reduce performance; keep OFF if matched tests show no benefit.

## Automated checks

Fabric/Knot loads the actual Minecraft 26.3 hooks without opening a game window or worlds. Wall tests cover 1,200 randomized volumes with independent interior projection checks, all axes/directions, the production traversal, near-camera walls, camera-inside bypass, interior holes, wall removal, edge reveals, deadline/work limits, and unknown cells. A synthetic arrangement resembling the recorded near-wall scene, including negative world coordinates, now produces a valid hiding proof; punching a hole makes it fail. Legacy single-cube geometry tests retain 2,337 accepted random volumes. Block/mod/pack guards, named/removed/overlay/global/custom-renderer bypasses, bounded target cache, clearing on toggles, and default-OFF state are also checked. These tests do not exercise a live GPU/world renderer, prove a performance benefit, or replace in-game visual validation and a Sodium benchmark.

## Why 0.1.52 showed no savings

The supplied recording shows Active with Hidden 0 / 108984 checks, 2016 probes and Check CPU about 0.003 ms/frame. That build required one cube to cover a large expanded target and ignored blockers within two blocks of the eye. Its recorded workload produced no skipped submissions, so no rendering savings can be credited to it. Version 0.1.53 changes the proof rather than advertising those zero-hit checks as an improvement.
