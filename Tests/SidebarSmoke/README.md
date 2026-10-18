# Launcher UI smoke tests

Run from the launcher directory:

```powershell
dotnet run --project Tests/SidebarSmoke/SidebarSmoke.csproj -p:UseSharedCompilation=false
```

These tests render the real compiled Avalonia views with fixture data. They do not
load real launcher settings/accounts or call Minecraft/Modrinth services.
Fixture directories and PNG previews go to a unique temporary directory.

Coverage: animated navigation, keyboard activation, responsive layout, live library
search/favorites, launch commands, modpack/content version selection, create/rename
dialogs, dependency permission choices, manager sections, installed-mod empty
states and live theme accents. Content removal tests cover mods (including disabled
mods), resource packs and shaders: real confirmation dialogs, cancellation, busy-game
guards, exact backup contents, duplicate filenames and outside-folder rejection.
Only generated fixture files are moved; no user content is touched.

QoL tests cover cross-window Undo notifications, restoring disabled mods, running
instance guards, share category defaults, invalidated previews, local ZIP export
and minimum-size window layouts. Run only these checks with `-- --qol`.

`-- --unified` tests the in-window instance workspace at its real minimum and default
content sizes: all sections, single mod list, update/Undo/import commands, embedded
catalog search/version selection, installing mods/resource packs/shaders through a
fixture-only HTTP client/cache, busy guards, state retention and disposal. No game,
real account or existing instance is touched. Full tests also cover the library's
direct navigation and unchanged animation/centering behavior.

Success prints `GAME_LIBRARY_UI_OK`, `REDESIGN_UI_OK` and the preview directory.

The gaming-library redesign checks persistent top navigation, list/hero selection,
mouse and keyboard activation, the correct Play target, busy-instance guards,
search/favorite/deletion selection reconciliation, built-in/custom/missing covers,
and layout at 960×600, 1280×800 and 1600×1000. The 500-instance fixture also
checks bounded list virtualization. Run just these checks with `-- --library`.
`-- --native-preview` opens an interactive Windows/macOS preview with fixture
data and fixture commands only; it never loads real accounts/settings/instances.

`-- --workspace` additionally exercises the redesigned settings categories,
profile layout at minimum size, hover/pressed surface transitions without text
scaling, cancelled/replaced entrances, reduced motion and the 24-animation
concurrency cap. Sampled intermediate frames check stationary readable pages,
one entrance per visual branch, and rapid tab switching. `-- --motion` isolates
the regression checks for preserved transforms/bound opacity, duplicate requests,
cancellation and disabling active motion. Full tests cover
the content catalogs, instance/mod managers, transfer history, create/rename,
dependency/removal confirmation, diagnostic and import preview windows.
The interactive fixture preview offers F6 (instance workspace), F7 (create
instance) and F8 (empty transfer center), without real instance data.

`-- --navigation` checks the top tabs' hover/pressed opacity, stationary labels,
full-height highlights, persistent selected state, and absence of horizontal underlines.
These checks are also included in the full suite.

`-- --alignment` checks centering of the search watermark, footer content,
ellipsis and favorite glyphs at 960×600 and 1280×800. Settings checks cover
immediate mouse selection, retained section controls with no detach/rebuild,
preserved edited values, and clearing inactive header backgrounds after rapid
switches. Timing output is diagnostic, not a cross-platform FPS guarantee.
