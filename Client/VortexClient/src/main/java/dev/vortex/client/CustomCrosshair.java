package dev.vortex.client;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.client.gui.GuiGraphicsExtractor;

public final class CustomCrosshair {
    public record Rect(int x, int y, int width, int height) { }
    private record Geometry(int shape, int size, int gap, int thickness, boolean dot) { }
    private static Geometry cachedGeometry;
    private static List<Rect> cachedRects = List.of();
    private static final int[] COLORS = {0xFFFFFF, 0x55FF55, 0xF1B9DF, 0x55FFFF, 0xFF5555, 0xFFFF55};
    private static final String[] COLOR_NAMES = {"White", "Green", "Pink", "Cyan", "Red", "Yellow"};
    /** The preset colours, in the order the swatches show them. */
    public static int[] palette() { return COLORS.clone(); }
    /** Restores the default look; whether the module is enabled is left alone. */
    public static void reset(ClientConfig c) {
        var defaults = new ClientConfig();
        c.crosshairShape = defaults.crosshairShape; c.crosshairColor = defaults.crosshairColor;
        c.crosshairSize = defaults.crosshairSize; c.crosshairGap = defaults.crosshairGap;
        c.crosshairThickness = defaults.crosshairThickness; c.crosshairOutline = defaults.crosshairOutline;
        c.crosshairDot = defaults.crosshairDot; c.crosshairOpacity = defaults.crosshairOpacity;
    }
    public static String shapeName(int shape) { return switch (Math.clamp(shape, 0, 2)) { case 1 -> "Dot"; case 2 -> "Circle"; default -> "Cross"; }; }
    public static String colorName(int color) {
        for (int i = 0; i < COLORS.length; i++) if (COLORS[i] == color) return COLOR_NAMES[i];
        return "Custom";
    }
    public static int nextColor(int color) {
        for (int i = 0; i < COLORS.length; i++) if (COLORS[i] == color) return COLORS[(i + 1) % COLORS.length];
        return COLORS[0];
    }
    public static List<Rect> geometry(int shape, int size, int gap, int thickness, boolean dot) {
        shape = Math.clamp(shape, 0, 2); size = Math.clamp(size, 2, 12);
        gap = Math.clamp(gap, 0, 8); thickness = Math.clamp(thickness, 1, 4);
        var rects = new ArrayList<Rect>();
        int low = -thickness / 2;
        if (shape == 0) {
            rects.add(new Rect(-gap - size, low, size, thickness));
            rects.add(new Rect(gap + 1, low, size, thickness));
            rects.add(new Rect(low, -gap - size, thickness, size));
            rects.add(new Rect(low, gap + 1, thickness, size));
        } else if (shape == 2) {
            int radius = size + gap, inner = Math.max(0, radius - thickness);
            for (int y = -radius; y <= radius; y++) for (int x = -radius; x <= radius; x++) {
                int distance = x * x + y * y;
                if (distance <= radius * radius && distance > inner * inner) rects.add(new Rect(x, y, 1, 1));
            }
        }
        if (shape == 1 || dot) {
            int width = shape == 1 ? Math.max(thickness, size / 2) : thickness;
            rects.add(new Rect(-width / 2, -width / 2, width, width));
        }
        return List.copyOf(rects);
    }
    public static void draw(GuiGraphicsExtractor g, int cx, int cy) {
        var c = VortexClient.config;
        var geometry = new Geometry(c.crosshairShape, c.crosshairSize, c.crosshairGap, c.crosshairThickness, c.crosshairDot);
        if (!geometry.equals(cachedGeometry)) {
            cachedGeometry = geometry;
            cachedRects = geometry(geometry.shape(), geometry.size(), geometry.gap(), geometry.thickness(), geometry.dot());
        }
        int alpha = (int)(c.crosshairOpacity * 255) << 24;
        if (c.crosshairOutline) for (var r : cachedRects)
            g.fill(cx + r.x() - 1, cy + r.y() - 1, cx + r.x() + r.width() + 1, cy + r.y() + r.height() + 1, alpha);
        for (var r : cachedRects)
            g.fill(cx + r.x(), cy + r.y(), cx + r.x() + r.width(), cy + r.y() + r.height(), alpha | c.crosshairColor);
    }
}
