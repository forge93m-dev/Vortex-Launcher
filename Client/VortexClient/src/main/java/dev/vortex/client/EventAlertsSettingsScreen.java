package dev.vortex.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

/** Which events make a sound and a notice, how loud, and when health counts as low. */
public final class EventAlertsSettingsScreen extends Screen {
    private final Screen parent;
    private boolean dirty, saveFailed;
    private final long opened = System.nanoTime();
    public EventAlertsSettingsScreen(Screen parent) {
        super(Minecraft.getInstance(), UiFonts.body(), Component.literal("Event alerts")); this.parent = parent;
    }
    private int w() { return Math.min(420, width - 16); }
    private int x() { return (width - w()) / 2; }
    private int y() { return Math.max(4, (height - 338) / 2); }
    private static String state(String label, boolean on) { return label + ": " + (on ? "ON" : "OFF"); }
    @Override protected void init() {
        int x = x(), y = y(), w = w(), row = y + 43;
        var c = VortexClient.config;
        addRenderableWidget(new ClientUi.ActionButton(x + 12, row, (w - 28) / 2, 24, Component.literal(state("Sounds", c.eventSounds)), b -> {
            c.eventSounds = !c.eventSounds; b.setMessage(Component.literal(state("Sounds", c.eventSounds))); dirty = true; save();
        }));
        addRenderableWidget(new ClientUi.ActionButton(x + 16 + (w - 28) / 2, row, (w - 28) / 2, 24, Component.literal("Test sound"),
            b -> EventAlerts.sound(EventAlerts.Kind.MENTION, c.eventVolume)));
        row += 32;
        addRenderableWidget(new ClientUi.Slider(x + 12, row, w - 24, "Volume", 0, 100, c.eventVolume,
            value -> Math.round(value) + "%", value -> { c.eventVolume = (int) Math.round(value); dirty = true; }, this::save));
        row += 34;
        for (var kind : EventAlerts.Kind.values()) {
            addRenderableWidget(new ClientUi.ActionButton(x + 12, row, w - 24, 22, Component.literal(state(kind.label, kind.on())), b -> {
                kind.toggle(); b.setMessage(Component.literal(state(kind.label, kind.on()))); dirty = true; save();
            }));
            row += 26;
        }
        addRenderableWidget(new ClientUi.Slider(x + 12, row, w - 24, "Low health at", 10, 50, c.lowHealthPercent,
            value -> Math.round(value) + "% of max", value -> { c.lowHealthPercent = (int) Math.round(value); dirty = true; }, this::save));
        addRenderableWidget(new ClientUi.ActionButton(x + w - 92, y + 338 - 34, 80, 22, Component.literal("Done"), b -> onClose(), true));
    }
    private void save() { if (dirty) { saveFailed = !VortexClient.config.save(); if (!saveFailed) dirty = false; } }
    @Override public void extractRenderState(GuiGraphicsExtractor g, int mx, int my, float delta) {
        ClientUi.beginPage(g, width, height, x(), y(), w(), 338, "Event alerts", "bell", opened);
        if (saveFailed) ClientUi.text(g, "Could not save. See game log.", x() + 12, y() + 338 - 28, w() - 112, ClientUi.ACCENT);
        super.extractRenderState(g, mx, my, delta);
        g.pose().popMatrix();
    }
    @Override public void onClose() { save(); if (!dirty) minecraft.gui.setScreen(parent); }
    @Override public void removed() { save(); }
    @Override public boolean isPauseScreen() { return false; }
    @Override public boolean isInGameUi() { return minecraft.level != null; }
    @Override public void extractBackground(GuiGraphicsExtractor g, int mx, int my, float delta) {
        if (isInGameUi()) minecraft.gui.hud.extractDeferredSubtitles();
        else extractPanorama(g, delta);
    }
}
