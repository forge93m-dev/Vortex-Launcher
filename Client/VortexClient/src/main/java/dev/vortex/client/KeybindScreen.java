package dev.vortex.client;

import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.network.chat.Component;

public final class KeybindScreen extends Screen {
    private final Screen parent;
    private ClientBindings.Action capture;
    private String message = "Click a binding, then press a key.";
    private boolean dirty;
    public KeybindScreen(Screen parent) { this(parent, null); }
    public KeybindScreen(Screen parent, ClientBindings.Action action) {
        super(Minecraft.getInstance(), UiFonts.body(), Component.literal("Keybinds")); this.parent = parent; capture = action;
    }
    public boolean capturing() { return capture != null; }
    @Override protected void setInitialFocus() { if (minecraft != null) super.setInitialFocus(); }
    private int panelWidth() { return Math.min(440, width - 16); }
    private int panelX() { return (width - panelWidth()) / 2; }
    private int panelHeight() { return Math.min(height-16,ClientBindings.Action.values().length*28+100); }
    private int rowStep() { return Math.min(28,(panelHeight()-100)/ClientBindings.Action.values().length); }
    private int panelY() { return (height - panelHeight()) / 2; }
    @Override protected void init() {
        int x = panelX(), y = panelY(), w = panelWidth();
        for (var action : ClientBindings.Action.values()) {
            addRenderableWidget(new ClientUi.ActionButton(x + 100, y + 40 + action.ordinal() * rowStep(), w - 112, Math.min(25,rowStep()-2),
                Component.literal(capture == action ? "Press a key..." : ClientBindings.label(ClientBindings.key(action))), b -> {
                    capture = action; message = "Esc: cancel / Delete: unbind"; rebuildWidgets();
                }));
        }
        addRenderableWidget(new ClientUi.ActionButton(x + 12, y + panelHeight()-32, 98, 22, Component.literal("Reset defaults"), b -> {
            ClientBindings.reset(); dirty = true; capture = null; message = "Default bindings restored."; save(); rebuildWidgets();
        }));
        addRenderableWidget(new ClientUi.ActionButton(x + w - 92, y + panelHeight()-32, 80, 22, Component.literal("Done"), b -> onClose(), true));
    }
    @Override public boolean keyPressed(KeyEvent event) {
        if (capture == null) return super.keyPressed(event);
        if (event.key() == InputConstants.KEY_ESCAPE) { capture = null; message = "Binding unchanged."; rebuildWidgets(); return true; }
        int key = event.key() == InputConstants.KEY_DELETE || event.key() == InputConstants.KEY_BACKSPACE ? -1 : event.key();
        if (!ClientBindings.valid(key)) { message = "Unknown key. Try another key."; return true; }
        var conflict = ClientBindings.conflict(capture, key);
        if (conflict != null) { message = "Already used by " + ClientBindings.title(conflict) + ". Pick another key."; return true; }
        String warning = "Binding saved.";
        if (key > 0 && minecraft != null && minecraft.options != null) {
            var pressed = InputConstants.Type.KEYBOARD.getOrCreate(key);
            for (var mapping : minecraft.options.keyMappings) if (mapping.matches(pressed)) {
                warning = "Also used by Minecraft. Choose a free key if needed."; break;
            }
        }
        ClientBindings.assign(capture, key); capture = null; dirty = true; message = warning; save(); rebuildWidgets(); return true;
    }
    private boolean save() {
        if (!dirty) return true;
        if (!VortexClient.config.save()) { message = "Could not save. See game log."; return false; }
        dirty = false; return true;
    }
    @Override public void extractRenderState(GuiGraphicsExtractor g, int mx, int my, float delta) {
        g.fill(0, 0, width, height, 0xA008090F);
        int x = panelX(), y = panelY(), w = panelWidth();
        ClientUi.panel(g, x, y, w, panelHeight(), 10, ClientUi.BORDER, ClientUi.PANEL);
        ClientUi.heading(g, "Keybinds", x + 12, y + 13, w - 24);
        for (var action : ClientBindings.Action.values()) ClientUi.text(g, ClientBindings.title(action), x + 12, y + 46 + action.ordinal() * rowStep(), 84, ClientUi.TEXT);
        ClientUi.text(g, message, x + 12, y + panelHeight()-52, w - 24, ClientUi.ACCENT);
        super.extractRenderState(g, mx, my, delta);
    }
    @Override public void onClose() { if (save()) minecraft.gui.setScreen(parent); }
    @Override public void removed() { save(); }
    @Override public boolean isPauseScreen() { return false; }
}
