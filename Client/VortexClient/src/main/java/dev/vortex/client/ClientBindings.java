package dev.vortex.client;

import com.mojang.blaze3d.platform.InputConstants;

public final class ClientBindings {
    public enum Action { MENU, ZOOM, FREELOOK, PARTY_MARKER, WAYPOINTS }
    private ClientBindings() { }
    public static int key(Action action) {
        var c = VortexClient.config;
        return switch (action) { case MENU -> c.menuKey; case ZOOM -> c.zoomKey; case FREELOOK -> c.freelookKey; case PARTY_MARKER -> c.partyMarkerKey; case WAYPOINTS -> c.waypointsKey; };
    }
    public static boolean valid(int key) { return key == -1 || key > 0 && key < 512; }
    public static boolean down(Action action) { int key = key(action); return key > 0 && InputConstants.isKeyDown(key); }
    public static String label(int key) {
        if (key < 1) return "Unbound";
        String saved = InputConstants.Type.KEYBOARD.getOrCreate(key).getName();
        return HudModules.biomeName(saved.replace("key.keyboard.", "").replace('.', '_'));
    }
    public static String title(Action action) { return switch (action) { case MENU -> "Vortex menu"; case ZOOM -> "Zoom"; case FREELOOK -> "Freelook"; case PARTY_MARKER -> "Party marker"; case WAYPOINTS -> "Waypoints"; }; }
    public static Action conflict(Action action, int key) {
        if (key < 1) return null;
        for (var other : Action.values()) if (other != action && key(other) == key) return other;
        return null;
    }
    public static boolean assign(Action action, int key) {
        if (!valid(key) || conflict(action, key) != null) return false;
        var c = VortexClient.config;
        switch (action) { case MENU -> c.menuKey = key; case ZOOM -> c.zoomKey = key; case FREELOOK -> c.freelookKey = key; case PARTY_MARKER -> c.partyMarkerKey = key; case WAYPOINTS -> c.waypointsKey = key; }
        VortexClient.ignoreMenuUntilRelease();
        return true;
    }
    public static void reset() {
        VortexClient.config.menuKey = InputConstants.KEY_RSHIFT;
        VortexClient.config.zoomKey = InputConstants.KEY_C;
        VortexClient.config.freelookKey = InputConstants.KEY_V;
        VortexClient.config.partyMarkerKey = InputConstants.KEY_G;
        VortexClient.config.waypointsKey = InputConstants.KEY_B;
        VortexClient.ignoreMenuUntilRelease();
    }
}
