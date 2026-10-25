package dev.vortex.client;

/**
 * Tells the skin hook on the tab-list entry (PlayerInfo) whether the player entity is already asking for the skin.
 * The entity applies the Vortex skin itself and must receive the plain one, or the override would be applied twice.
 * Other readers of the entry (chat-head mods, for example) get the Vortex skin directly.
 */
public final class SkinCalls {
    private static final ThreadLocal<int[]> DEPTH = ThreadLocal.withInitial(() -> new int[1]);
    private SkinCalls() { }
    public static void enter() { DEPTH.get()[0]++; }
    public static void leave() { int[] depth = DEPTH.get(); if (depth[0] > 0) depth[0]--; }
    public static boolean inside() { return DEPTH.get()[0] > 0; }
}
