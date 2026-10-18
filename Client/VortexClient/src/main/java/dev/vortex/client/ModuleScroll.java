package dev.vortex.client;

/** Shared pure scroll math for mouse wheel, trackpad and scrollbar. */
public final class ModuleScroll {
    private ModuleScroll() { }
    public static int maximum(int count, int columns, int cardHeight, int viewportHeight) {
        int rows = (Math.max(0, count) + Math.max(1, columns) - 1) / Math.max(1, columns);
        return Math.max(0, rows * (cardHeight + 8) - (rows == 0 ? 0 : 8) - Math.max(1, viewportHeight));
    }
    public static double wheel(double offset, double delta, int maximum) {
        if (!Double.isFinite(delta)) return offset;
        return Math.clamp(offset - Math.clamp(delta, -100, 100) * 36, 0, maximum);
    }
    public static boolean intersects(int top, int height, int viewportTop, int viewportBottom) {
        return top < viewportBottom && top + height > viewportTop;
    }
    public static int thumbHeight(int viewportHeight, int maximum) {
        return Math.min(viewportHeight, Math.max(18, (int)Math.round((double)viewportHeight * viewportHeight / (viewportHeight + maximum))));
    }
    public static double drag(double thumbTop, int viewportHeight, int maximum) {
        int travel = viewportHeight - thumbHeight(viewportHeight, maximum);
        return travel <= 0 ? 0 : Math.clamp(thumbTop / travel, 0, 1) * maximum;
    }
}
