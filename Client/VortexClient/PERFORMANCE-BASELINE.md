# Live diagnostic session — 2026-10-18

Minecraft 26.3, existing local singleplayer session. No graphics settings, game files or simulation behavior were changed.

The first usable recording lasted 45 seconds. Its built-in client FPS events were **10 for all 45 samples**. The render thread accumulated approximately **42.88 seconds of parked time** and its stacks included `FramerateLimiter.limitDisplayFPS`. This is consistent with a background/inactive-window frame cap and is not a valid maximum-FPS baseline.

Integrated-server tick events averaged **3.263 ms**, maximum **3.915 ms**. Four GC collections reported **12.766 ms** of total pause time, longest **4.344 ms**. These observations do not establish a severe server-tick or GC bottleneck in this particular capped scene. Native samples in idle HTTP selectors represent blocking waits, not evidence of high CPU consumption.

Allocation event weights are sampling estimates and may include startup/first-sample bias. They are not presented as exact memory usage or an FPS bottleneck. No optimization benefit has been measured or claimed.

## Delayed foreground recording

The second recording lasted 45 seconds. Its initial ten one-second FPS samples still showed the 10 FPS background cap. The remaining 35 samples averaged **193.09 FPS**, median **193**, range **103–247**. The first transition sample was 103; subsequent samples ranged **163–247**. These are one-second FPS counters, not individual frame times or 1% lows. Different camera positions, movement and chunk loading mean they are not a controlled benchmark.

Integrated-server tick events over the whole recording averaged **3.941 ms**, maximum **5.721 ms**, below the 50 ms budget for 20 TPS. Fourteen GC collections reported **72.427 ms** of aggregate pauses, longest **8.932 ms**. This does not identify server ticks or GC as the dominant frame-rate bottleneck in this scene.

The render-thread samples frequently include `VulkanCommandEncoder.awaitSubmitCompletion` / `vkWaitSemaphores` (376 of 784 combined Java/native render-thread samples). This is evidence of graphics submission synchronization, **not a direct measurement of GPU utilization**. Native and foreign-call sampling can include waits; counts must not be equated to CPU utilization or exact wall-time percentages. Rendering/submission and driver/GPU behavior are the leading areas to investigate before claiming a CPU-side patch will materially increase this scene's FPS.

The active game's Vortex entry class was loaded. A targeted class-hierarchy query did not find the checked Sodium entry class. No gameplay/rendering patch, graphics setting change or before/after improvement has been applied or claimed. Compatibility and additive benefit with Sodium still require separate matched-scene tests.

A conservative visibility/occlusion optimization is a possible next investigation: avoid submitting fully hidden objects while preserving visible geometry, names, glow and effects. It is not implemented here, is not a proven fix for this scene, and requires correctness and rendering-stack compatibility tests. CPU allocation reductions may be useful separately, but cannot be advertised as an FPS gain on the basis of the sampling weights alone.

Recordings stay local under the task's generated diagnostics directory outside instances. Environment variables, JVM properties/arguments and exception payloads were explicitly disabled. The summary tool reads only performance fields, method/type names and thread names, never item/world/chat contents or tokens. The foreground recording also disables Minecraft's network/packet/region detail events. No recordings are uploaded automatically.
