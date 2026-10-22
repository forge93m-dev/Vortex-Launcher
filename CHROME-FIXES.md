# Launcher 0.1.5: rounded chrome and bounded card hover

Custom windows request per-pixel transparency (not Acrylic/Blur), keep the native
background transparent, and draw an opaque themed body clipped to 12 px corners.
An explicit rounded geometry clips opaque child backgrounds and cover images:
`Border.CornerRadius` plus `ClipToBounds` alone is insufficient in Avalonia 11.0.6.
Geometry updates only on bounds/radius changes, not every animation frame.

Maximized/fullscreen windows use square corners and no outer border. From 0.1.18,
Windows without transparency uses a native rounded HWND region over the client
surface, so its corners do not silently become square. The native region refreshes
on size/DPI changes and is removed on maximize/fullscreen. Successful region calls
transfer ownership to Windows; rejected regions are disposed. Other unsupported
platforms retain a square theme-colored fallback, not black/white corner patches.
From 0.1.19 the native clip is also applied when Windows reports transparency:
reported compositor support alone does not prove that the visible outer corners
are transparent. A normal HWND must always have the matching rounded region.
The shared title-bar windows and the
instance manager use the same chrome behavior. No theme or user settings change.

From 0.1.20 a separate inner content clip preserves the curved border: clipping
only the outer shell allowed rectangular child backgrounds to paint over its
stroke. The inner radius is the outer radius minus border thickness and becomes
zero on maximize/fullscreen. Pixel regressions check the stroke at all four arcs
with deliberately contrasting opaque child content, as well as corner alpha.

Library cards retain the 2 px hover lift and 272×154 px size. A 4 px top inset
and 8 px bottom inset reserve animation room within each unchanged 284×166 px
virtualized grid slot. Scrolling/clipping remains enabled; cards cannot paint
over neighboring launcher sections. Cover art is clipped to the card outline.

Headless regressions verify four transparent outer corner pixels and an opaque
center despite an opaque child, maximize/restore/fallback shapes, and hovered
first-row/last-row/right-edge card bounds. They preserve the existing 500-item
virtualization regression. These tests validate rendered geometry and requested
transparency, not an interactive OS-compositor screenshot.

`WindowsRegionChecks` also exercises real hidden Win32 HWNDs (with/without resize
frames): all four client corners, preserved center, 200% DPI, resize, region clearing
and 200 replacements without GDI-object growth. No user window is captured or moved.

Quick check: `dotnet run --project Tests/SidebarSmoke/SidebarSmoke.csproj -- --chrome`.

Reference implementation version:
[Avalonia TopLevel 11.0.6](https://github.com/AvaloniaUI/Avalonia/blob/11.0.6/src/Avalonia.Controls/TopLevel.cs).
Public release/update manifest is not changed by this patch.
