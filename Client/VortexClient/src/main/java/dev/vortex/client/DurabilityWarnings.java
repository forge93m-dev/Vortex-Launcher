package dev.vortex.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.world.entity.EquipmentSlot;

public final class DurabilityWarnings {
    private static final EquipmentSlot[] ARMOR = {EquipmentSlot.HEAD, EquipmentSlot.CHEST, EquipmentSlot.LEGS, EquipmentSlot.FEET};
    private static final Gate GATE = new Gate();
    private static Object player, level;
    private static String message;
    private static long expires;
    private static boolean wasEnabled;
    private DurabilityWarnings() { }
    public static boolean low(int maximum, int damage, int threshold) {
        return maximum > 0 && (long)(maximum - Math.clamp(damage, 0, maximum)) * 100 <= (long)maximum * Math.clamp(threshold, 1, 30);
    }
    public static int percentage(int maximum, int damage) {
        return maximum <= 0 ? 100 : (int)((long)(maximum - Math.clamp(damage, 0, maximum)) * 100 / maximum);
    }
    public static void tick(Minecraft mc) {
        if (player != mc.player || level != mc.level || wasEnabled != VortexClient.config.durabilityAlerts) {
            GATE.reset(); message = null; player = mc.player; level = mc.level; wasEnabled = VortexClient.config.durabilityAlerts;
        }
        if (!wasEnabled || mc.player == null || mc.level == null || !mc.player.isAlive() ||
            mc.gui.screen() != null || mc.gui.hud.isHidden() || !mc.isWindowActive()) return;
        long now = System.nanoTime();
        for (int slot = 0; slot < 6; slot++) {
            var stack = slot < 4 ? mc.player.getItemBySlot(ARMOR[slot]) : slot == 4 ? mc.player.getMainHandItem() : mc.player.getOffhandItem();
            int max = stack.isDamageableItem() ? stack.getMaxDamage() : 0, damage = stack.getDamageValue();
            if (GATE.check(slot, stack.isEmpty() ? null : stack.getItem(), max, damage, VortexClient.config.durabilityThreshold, now / 1_000_000)) {
                String name = HudModules.biomeName(BuiltInRegistries.ITEM.getKey(stack.getItem()).getPath());
                message = "Low durability: " + name + " (" + percentage(max, damage) + "%)";
                expires = now + 5_000_000_000L;
            }
        }
    }
    public static void draw(GuiGraphicsExtractor g) {
        var mc = Minecraft.getInstance();
        if (!VortexClient.config.durabilityAlerts || message == null || System.nanoTime() >= expires || mc.player == null ||
            mc.gui.hud.isHidden() || mc.gui.screen() != null) return;
        var font = UiFonts.body();
        String text = font.plainSubstrByWidth(message, Math.max(0, g.guiWidth() - 20));
        g.text(font, text, (g.guiWidth() - font.width(text)) / 2, 12, 0xFFFFB878, true);
    }
    /** One notice per low item, 30s global cooldown; repairs re-arm with 2% hysteresis. */
    public static final class Gate {
        private final Object[] items = new Object[6];
        private final boolean[] reported = new boolean[6];
        private boolean hasNotice;
        private long lastNotice;
        public void reset() { java.util.Arrays.fill(items, null); java.util.Arrays.fill(reported, false); hasNotice = false; }
        public boolean check(int slot, Object item, int maximum, int damage, int threshold, long now) {
            if (slot < 0 || slot >= items.length) return false;
            if (!java.util.Objects.equals(items[slot], item)) { items[slot] = item; reported[slot] = false; }
            if (maximum <= 0 || percentage(maximum, damage) > threshold + 2) { reported[slot] = false; return false; }
            if (!low(maximum, damage, threshold) || reported[slot]) return false;
            if (hasNotice && now >= lastNotice && now - lastNotice < 30_000) return false;
            reported[slot] = true; hasNotice = true; lastNotice = now; return true;
        }
    }
}
