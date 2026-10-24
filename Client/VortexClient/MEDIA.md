# Music and YouTube removed

Music, the YouTube Music Desktop / Companion integration, system media controls and
the embedded YouTube video player have all been removed at the user's request.
There are no media buttons, music key binding, overlays, desktop app startup,
Companion authentication/networking, browser engine installation/downloads or media
render/tick hooks. The optional browser bridge/dependency manifest are not packaged.
Browser-specific macOS AWT opens have been removed from launcher JVM arguments;
native access for the unrelated Discord IPC/JNA integration remains intact.

Old media configuration keys are ignored, so existing JSON cannot reactivate these
features or reset unrelated preferences. Source builds use fresh generated directories
for ports, preventing stale deleted Java files from reappearing in a candidate.
ResourceToolsSmoke checks the actual packaged classes, removed settings, missing
browser resources and old-config migration across supported Minecraft targets.

The user's external applications, account, desktop settings and playback were not
changed or stopped. Previously installed .vortex-media caches and machine-local
Companion authorization files were left untouched, but no code reads or executes them.
Only the implementation and bundled browser bridge were removed from this project.
The user may revoke Vortex Client in YouTube Music Desktop's authorized companions.
