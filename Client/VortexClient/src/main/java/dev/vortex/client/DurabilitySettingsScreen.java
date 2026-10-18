package dev.vortex.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

public final class DurabilitySettingsScreen extends Screen {
    private final Screen parent;
    private boolean dirty;
    private boolean saveFailed;
    public DurabilitySettingsScreen(Screen parent) {
        super(Minecraft.getInstance(), UiFonts.body(), Component.literal("Durability alerts")); this.parent = parent;
    }
    private int w() { return Math.min(420, width - 16); }
    private int x() { return (width - w()) / 2; }
    private int y() { return (height - 186) / 2; }
    @Override protected void init() {
        int x = x(), y = y(), w = w();
        addRenderableWidget(new ClientUi.ActionButton(x + 12, y + 43, w - 24, 24,
            Component.literal(VortexClient.config.durabilityAlerts ? "Alerts: ON" : "Alerts: OFF"), b -> {
                VortexClient.config.durabilityAlerts = !VortexClient.config.durabilityAlerts;
                b.setMessage(Component.literal(VortexClient.config.durabilityAlerts ? "Alerts: ON" : "Alerts: OFF")); dirty = true;
            }));
        addRenderableWidget(new ClientUi.Slider(x + 12, y + 77, w - 24, "Warn below", 1, 30, VortexClient.config.durabilityThreshold,
            value -> Math.round(value) + "%", value -> { VortexClient.config.durabilityThreshold = (int)Math.round(value); dirty = true; }, this::save));
        addRenderableWidget(new ClientUi.ActionButton(x + w - 92, y + 152, 80, 22, Component.literal("Done"), b -> onClose(), true));
    }
    private void save() { if (dirty) { saveFailed = !VortexClient.config.save(); if (!saveFailed) dirty = false; } }
    @Override public void extractRenderState(GuiGraphicsExtractor g, int mx, int my, float delta) {
        g.fill(0, 0, width, height, 0xA008090F);
        ClientUi.panel(g, x(), y(), w(), 186, 10, ClientUi.BORDER, ClientUi.PANEL);
        ClientUi.heading(g, "Durability alerts", x() + 12, y() + 13, w() - 24);
        ClientUi.text(g, saveFailed ? "Could not save. See game log." : "Armor and held items / no repeated spam", x() + 12, y() + 125, w() - 24, saveFailed ? ClientUi.ACCENT : ClientUi.MUTED);
        super.extractRenderState(g, mx, my, delta);
    }
    @Override public void onClose() { save(); if (!dirty) minecraft.gui.setScreen(parent); }
    @Override public void removed() { save(); }
    @Override public boolean isPauseScreen() { return false; }
}
