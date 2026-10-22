# Compact desktop library

The library no longer uses a large selected-instance banner and horizontal carousel. It now has a vertically scrolling responsive grid, persistent search and favorites filters, direct Play/Manage actions on each tile, and a compact selected-instance toolbar. Existing update-client, content, cover, menu, recovery and workspace routes are retained. No instance metadata or game files are migrated by this UI change.

`Views/InstanceTileGrid.cs` groups items into responsive fixed-height rows and virtualizes those rows through Avalonia's ItemsRepeater/StackLayout. Only visible rows plus a small buffer construct artwork controls. Recycled row contents and selection handlers are rebuilt when their data changes, so resize/filter/favorite operations do not retain old instance targets. The scroll bar remains visible while scrolling is needed. Navigation supports arrow keys, Home/End and Page Up/Down.

`Tests/SidebarSmoke/GridBrowsingChecks.cs` exercises 500 instances: bounded realization, vertical-only extent, actual mouse-wheel input, direct tile Play, grid-row keyboard navigation, full visibility at the end and middle, return to the start, exact row count, recycled selection and the minimum desktop window. Existing library, workspace, chrome/hover, navigation and alignment checks continue to cover their original routes.
