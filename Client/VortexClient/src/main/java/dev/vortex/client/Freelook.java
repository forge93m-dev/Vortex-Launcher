package dev.vortex.client;

import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.CameraType;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;

/** View-only rotation. Never writes player angles, sends packets or saves camera options. */
public final class Freelook {
    private static final View VIEW = new View();
    private static CameraType previous;
    private static Object player, level;
    private static boolean waitForRelease;
    private Freelook() { }
    public static void sync(Minecraft mc) {
        if (mc == null || mc.options == null) return;
        boolean allowed = VortexClient.config.freelookEnabled && mc.player != null && mc.level != null &&
            mc.player.isAlive() && !mc.player.isSleeping() && !mc.player.isSpectator() &&
            mc.getCameraEntity() == mc.player && mc.gui.screen() == null && mc.gui.overlay() == null &&
            mc.isWindowActive() && mc.mouseHandler.isMouseGrabbed();
        boolean pressed = mc.isWindowActive() && ClientBindings.down(ClientBindings.Action.FREELOOK);
        if (!pressed) waitForRelease = false;
        if (VIEW.active() && (!allowed || !pressed || player != mc.player || level != mc.level || mc.options.getCameraType() != CameraType.THIRD_PERSON_BACK)) {
            stop(mc);
            waitForRelease = pressed;
            return;
        }
        if (allowed && pressed && !waitForRelease && !VIEW.active()) {
            player = mc.player; level = mc.level; previous = mc.options.getCameraType();
            VIEW.begin(mc.player.getYRot(), mc.player.getXRot());
            mc.options.setCameraType(CameraType.THIRD_PERSON_BACK);
        }
    }
    public static boolean turn(LocalPlayer entity, double dx, double dy) {
        var mc = Minecraft.getInstance();
        sync(mc);
        if (!VIEW.active() || entity != player) return false;
        VIEW.turn(dx, dy, VortexClient.config.freelookSensitivity);
        return true;
    }
    public static float yaw(float vanilla) { return VIEW.active() ? VIEW.yaw() : vanilla; }
    public static float pitch(float vanilla) { return VIEW.active() ? VIEW.pitch() : vanilla; }
    public static void stop(Minecraft mc) {
        if (mc != null && mc.options != null)
            mc.options.setCameraType(restoreCamera(previous, mc.options.getCameraType(), VIEW.active()));
        VIEW.end(); previous = null; player = level = null;
    }
    public static CameraType restoreCamera(CameraType original, CameraType current, boolean active) {
        return active && current == CameraType.THIRD_PERSON_BACK && original != null ? original : current;
    }
    public static String keyName(int key) {
        return ClientBindings.label(key);
    }
    public static int nextKey(int key) {
        return switch (key) { case InputConstants.KEY_V -> InputConstants.KEY_G; case InputConstants.KEY_G -> InputConstants.KEY_RALT; default -> InputConstants.KEY_V; };
    }
    public static final class View {
        private boolean active;
        private float yaw, pitch;
        public boolean active() { return active; }
        public float yaw() { return yaw; }
        public float pitch() { return pitch; }
        public void begin(float yaw, float pitch) {
            this.yaw = wrap(yaw); this.pitch = Float.isFinite(pitch) ? Math.clamp(pitch, -90, 90) : 0; active = true;
        }
        public void end() { active = false; }
        public void turn(double dx, double dy, float sensitivity) {
            if (!active || !Double.isFinite(dx) || !Double.isFinite(dy) || !Float.isFinite(sensitivity)) return;
            double factor = .15 * Math.clamp(sensitivity, .25f, 2);
            yaw = wrap(yaw + dx * factor);
            pitch = (float)Math.clamp(pitch + dy * factor, -90, 90);
        }
        public static float wrap(double value) {
            if (!Double.isFinite(value)) return 0;
            return (float)(((value + 180) % 360 + 360) % 360 - 180);
        }
    }
}
