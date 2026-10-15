# Launcher 0.1.5: rounded chrome and bounded card hover

Custom windows request per-pixel transparency (not Acrylic/Blur), keep the native
background transparent, and draw an opaque themed body clipped to 12 px corners.
An explicit rounded geometry clips opaque child backgrounds and cover images:
`Border.CornerRadius` plus `ClipToBounds` alone is insufficient in Avalonia 11.0.6.
Geometry updates only on bounds/radius changes, not every animation frame.

Maximized/fullscreen windows use square corners and no outer border. If the
platform cannot provide transparency, the fallback is a square theme-colored
surface, not black/white corner patches. The shared title-bar windows and the
instance manager use the same chrome behavior. No theme or user settings change.

Library cards retain the 2 px hover lift and 272×154 px size. A 4 px top inset
and 8 px bottom inset reserve animation room within each unchanged 284×166 px
virtualized grid slot. Scrolling/clipping remains enabled; cards cannot paint
over neighboring launcher sections. Cover art is clipped to the card outline.

Headless regressions verify four transparent outer corner pixels and an opaque
center despite an opaque child, maximize/restore/fallback shapes, and hovered
first-row/last-row/right-edge card bounds. They preserve the existing 500-item
virtualization regression. These tests validate rendered geometry and requested
transparency, not an interactive OS-compositor screenshot.

Quick check: `dotnet run --project Tests/SidebarSmoke/SidebarSmoke.csproj -- --chrome`.

Reference implementation version:
[Avalonia TopLevel 11.0.6](https://github.com/AvaloniaUI/Avalonia/blob/11.0.6/src/Avalonia.Controls/TopLevel.cs).
Public release/update manifest is not changed by this patch.

