# Local waypoints

Press **B** in a world to open Waypoints. Change this binding in Keys if another mod uses B.

- Create a marker at your current position or enter integer XYZ coordinates.
- Give it a name and choose its accent color.
- Select an existing entry to edit it. Hide / Show keeps the entry saved.
- Remove requires a second confirmation click.
- In-world pins show the name and distance; offscreen pins indicate direction.
- There is no distance cutoff. F1 and the Waypoints module toggle hide the display.
- Each world/server and dimension has its own entries. Moving a singleplayer world to another folder changes its identity.

Storage: `%USERPROFILE%/.vortex/waypoints-v1.json`, outside portable instances. These are private local markers, not party markers; coordinates are not sent online. Back up this file to keep your markers. A 512-entry storage safety ceiling is separate from rendering distance. Corrupt/unsupported originals are preserved rather than overwritten.

Disk operations run off the render thread. Writes use a shared file lock, reload the latest entries before modification and atomically replace the file where supported.

Automated coverage checks persistence, rename/hide/show/removal, independent writers, malformed-file preservation, validation, world/dimension isolation, projection at long distances, offscreen indicators and scaled layout geometry. The Fabric suite checks actual 26.3 mixin linkage. These checks do not replace visual in-game testing.
