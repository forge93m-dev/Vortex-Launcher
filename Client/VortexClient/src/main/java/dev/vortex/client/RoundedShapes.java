package dev.vortex.client;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.resources.Identifier;

/** Shared atlas, no per-pixel geometry or per-frame texture generation. */
public final class RoundedShapes {
    private static final Identifier ATLAS = Identifier.fromNamespaceAndPath("vortex_client", "textures/ui/corners.png");
    private RoundedShapes() { }
    public static int radius(int width, int height, int radius) {
        return Math.clamp(radius, 0, Math.max(0, Math.min(12, Math.min(width, height) / 2)));
    }
    public static int drawOperations(int width, int height, int radius) {
        if (width <= 0 || height <= 0) return 0;
        int r = radius(width, height, radius);
        return r == 0 ? 1 : 4 + (width > r * 2 ? 1 : 0) + (height > r * 2 ? 2 : 0);
    }
    public static void draw(GuiGraphicsExtractor g, int x, int y, int w, int h, int radius, int color) {
        if (w <= 0 || h <= 0 || (color >>> 24) == 0) return;
        int r = radius(w, h, radius);
        if (r == 0) { g.fill(x, y, x + w, y + h, color); return; }
        // Three non-overlapping solid strips + four alpha-mask corners (max seven quads).
        if (w > r * 2) g.fill(x + r, y, x + w - r, y + h, color);
        if (h > r * 2) {
            g.fill(x, y + r, x + r, y + h - r, color);
            g.fill(x + w - r, y + r, x + w, y + h - r, color);
        }
        int index = r - 1, u = index % 4 * 128 + 4, v = index / 4 * 128 + 4, cut = r * 4;
        corner(g, x, y, r, u, v, cut, color);
        corner(g, x + w - r, y, r, u + cut, v, cut, color);
        corner(g, x, y + h - r, r, u, v + cut, cut, color);
        corner(g, x + w - r, y + h - r, r, u + cut, v + cut, cut, color);
    }
    private static void corner(GuiGraphicsExtractor g, int x, int y, int size, int u, int v, int cut, int color) {
        g.blit(RenderPipelines.GUI_TEXTURED, ATLAS, x, y, u, v, size, size, cut, cut, 512, 384, color);
    }
}
