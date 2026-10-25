package dev.vortex.client;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;

/**
 * Soft opening for in-game menus (inventory, chests, furnaces...): the dim fades in and the panel eases up
 * from 96% size. Only the first ~220 ms change; afterwards everything is exactly vanilla. Off with UI animations.
 */
public final class MenuMotion {
    private MenuMotion() { }
    /** Vanilla's in-game menu dim, top and bottom of the gradient. */
    public static final int DIM_TOP = 0xC0101010, DIM_BOTTOM = 0xD0101010;
    private static Screen current;
    private static long opened;
    /** Opening progress 0..1 for a screen; a different screen restarts it. */
    public static float progress(Screen screen) {
        if (screen != current) { current = screen; opened = System.nanoTime(); }
        return UiMotion.progress(opened);
    }
    /** A colour's alpha scaled by the opening progress. */
    public static int fade(int color, float progress) {
        return (int)((color >>> 24) * Math.clamp(progress, 0, 1)) << 24 | color & 0xFFFFFF;
    }
    public static void dim(GuiGraphicsExtractor g, Screen screen, int width, int height) {
        float progress = progress(screen);
        g.fillGradient(0, 0, width, height, fade(DIM_TOP, progress), fade(DIM_BOTTOM, progress));
    }
    /** Panel scale and lift while opening: from 96% and 6 px lower to full size in place. */
    public static float scale(float progress) { return .96f + .04f * progress; }
    public static float lift(float progress) { return (1 - progress) * 6; }
    public static void beginPanel(GuiGraphicsExtractor g, Screen screen, int width, int height) {
        float progress = progress(screen);
        g.pose().pushMatrix();
        g.pose().translate(width / 2f, height / 2f + lift(progress));
        g.pose().scale(scale(progress), scale(progress));
        g.pose().translate(-width / 2f, -height / 2f);
    }
    public static void endPanel(GuiGraphicsExtractor g) { g.pose().popMatrix(); }
}
