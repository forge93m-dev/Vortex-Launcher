package dev.vortex.client;

import com.mojang.blaze3d.platform.InputConstants;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.PauseScreen;
import net.minecraft.client.gui.screens.TitleScreen;
import org.slf4j.LoggerFactory;

public final class VortexClient implements ClientModInitializer {
    public static ClientConfig config = new ClientConfig();
    private static boolean menuWasDown;
    /** Strongest zoom: 12x (was 8x). */
    public static final float MAX_ZOOM = 12;
    private static float zoomFactor = 1;
    private static boolean zoomToggled, zoomKeyWasDown;
    private static long previousZoomFrame;
    private static boolean zoomSettingsDirty;
    public static void ignoreMenuUntilRelease() { menuWasDown = true; }

    @Override
    public void onInitializeClient() {
        config = ClientConfig.load(FabricLoader.getInstance().getConfigDir().resolve("vortex-client.json"));
        ClientUi.applyAccent(config.clientAccent);
        LoggerFactory.getLogger("Vortex Client").info("Vortex Client 0.2.5 initialized. Resource Counter and remembered storage tools; experimental visibility optimization remains disabled.");
    }

    public static void tick(Minecraft minecraft) {
        DiscordPresence.tick(minecraft);
        ClientNotifications.context(LocalSkins.profile(minecraft));
        LocalSkins.tick(minecraft);
        WardrobeAutoSync.tick(minecraft);
        Capes.tick(minecraft);
        PartyFeature.tick(minecraft);
        WaypointsFeature.tick(minecraft);
        WaypointBlocks.emit(minecraft);
        ChatTabs.tick(minecraft);
        ResourceCounter.tick(minecraft);
        StorageMemory.tick(minecraft);
        DeathWaypoints.tick(minecraft);
        EntityHearts.tick(minecraft);
        MetricsHud.tick(minecraft);
        Freelook.sync(minecraft);
        DurabilityWarnings.tick(minecraft);
        EventAlerts.tick(minecraft);
        boolean down = minecraft.isWindowActive() && ClientBindings.down(ClientBindings.Action.MENU);
        var screen = minecraft.gui.screen();
        if (down && !menuWasDown && minecraft.gui.overlay() == null && !(screen instanceof KeybindScreen keys && keys.capturing())) {
            if (screen instanceof HudEditorScreen editor) editor.onClose();
            else if (screen instanceof VortexScreen vortex) vortex.onClose();
            else if (screen instanceof VortexHomeScreen home) home.onClose();
            else if (screen == null || screen instanceof TitleScreen || screen instanceof PauseScreen)
                minecraft.gui.setScreen(new VortexHomeScreen(screen));
        }
        menuWasDown = down;
        // Toggle mode: one press turns zoom on, the next turns it off; leaving the world or opening a menu turns it off.
        boolean zoomKey = minecraft.isWindowActive() && ClientBindings.down(ClientBindings.Action.ZOOM);
        if (config.zoomToggle && zoomKey && !zoomKeyWasDown && minecraft.gui.screen() == null) zoomToggled = !zoomToggled;
        if (!config.zoomToggle || minecraft.level == null || minecraft.gui.screen() != null) zoomToggled = false;
        zoomKeyWasDown = zoomKey;
        if (zoomSettingsDirty && !isZoomActive(minecraft)) {
            zoomSettingsDirty = false;
            config.save();
        }
    }

    public static boolean useToggleSprint(String keyName, boolean vanilla) {
        return vanilla || (config.toggleSprintEnabled && "key.sprint".equals(keyName));
    }

    private static boolean isZoomActive(Minecraft minecraft) {
        return config.zoomEnabled && minecraft.level != null && minecraft.gui.screen() == null &&
            minecraft.gui.overlay() == null && minecraft.isWindowActive() &&
            (config.zoomToggle ? zoomToggled : ClientBindings.down(ClientBindings.Action.ZOOM));
    }

    public static boolean scrollZoom(long window, double vertical) {
        Minecraft minecraft = Minecraft.getInstance();
        if (window == 0 || window != minecraft.getWindow().handle() ||
            !Double.isFinite(vertical) || vertical == 0 || !isZoomActive(minecraft)) return false;
        float adjusted = adjustZoomStrength(config.zoomMultiplier, vertical);
        if (adjusted != config.zoomMultiplier) {
            config.zoomMultiplier = adjusted;
            zoomSettingsDirty = true;
        }
        // Consume even at the limits: scrolling during Zoom must not move the hotbar.
        return true;
    }

    public static float adjustZoomStrength(float current, double scroll) {
        if (!Double.isFinite(scroll) || !Float.isFinite(current)) return Float.isFinite(current) ? current : 4;
        // Fractional deltas keep trackpads smooth; positive scroll zooms in.
        return (float)Math.clamp(current * Math.pow(1.15, Math.clamp(scroll, -50, 50)), 2, MAX_ZOOM);
    }

    public static float zoom(float originalFov) {
        Minecraft minecraft = Minecraft.getInstance();
        boolean active = isZoomActive(minecraft);
        long now = System.nanoTime();
        double elapsed = previousZoomFrame == 0 ? 0 : Math.clamp((now - previousZoomFrame) / 1e9, 0, 0.1);
        previousZoomFrame = now;
        float target = active ? 1 / config.zoomMultiplier : 1;
        zoomFactor += (target - zoomFactor) * (float)(1 - Math.exp(-elapsed * 18));
        if (Math.abs(zoomFactor - target) < 0.001f) zoomFactor = target;
        return originalFov * zoomFactor;
    }

    /** Mouse turn multiplier: while zoomed in, turning slows down with the field of view so aiming stays steady. */
    public static double zoomTurnScale() {
        return config.zoomEnabled && config.zoomSlowMouse ? Math.clamp(zoomFactor, 1 / MAX_ZOOM, 1) : 1;
    }
    public static void drawFps(GuiGraphicsExtractor graphics, boolean preview) {
        Minecraft minecraft = Minecraft.getInstance();
        if (!config.fpsEnabled || (!preview && (minecraft.level == null || minecraft.gui.hud.isHidden()))) return;
        String text = minecraft.getFps() + " FPS";
        float scale = HudModules.scale(HudModules.Element.FPS);
        var bounds = HudModules.bounds(HudModules.Element.FPS, graphics.guiWidth(), graphics.guiHeight());
        int x = bounds.x();
        int y = bounds.y();
        int alpha = (int)(HudModules.opacity(HudModules.Element.FPS) * 255) << 24;
        var pose = graphics.pose();
        pose.pushMatrix();
        pose.translate(x, y);
        pose.scale(scale, scale);
        graphics.text(minecraft.font, text, 7, 7, alpha | 0xFFFFFF, true);
        pose.popMatrix();
    }

    public static void drawZoom(GuiGraphicsExtractor graphics) {
        Minecraft minecraft = Minecraft.getInstance();
        if (!isZoomActive(minecraft) || minecraft.gui.hud.isHidden()) return;
        // A small pill under the crosshair: strength and a bar from 2x to 12x.
        var font = UiFonts.body();
        String label = String.format(java.util.Locale.ROOT, "%.1f×", config.zoomMultiplier), hint = "scroll";
        int w = Math.max(64, font.width(label) + font.width(hint) + 26), x = graphics.guiWidth() / 2 - w / 2, y = graphics.guiHeight() / 2 + 18;
        ClientUi.pill(graphics, x, y, w, 18, 0xC015161C);
        graphics.text(font, label, x + 9, y + 3, ClientUi.TEXT, false);
        graphics.text(font, hint, x + w - font.width(hint) - 9, y + 3, ClientUi.MUTED, false);
        float amount = (config.zoomMultiplier - 2) / (MAX_ZOOM - 2);
        ClientUi.rounded(graphics, x + 8, y + 13, w - 16, 2, 1, ClientUi.TRACK);
        ClientUi.rounded(graphics, x + 8, y + 13, Math.max(2, Math.round((w - 16) * amount)), 2, 1, ClientUi.ACCENT);
    }
}
