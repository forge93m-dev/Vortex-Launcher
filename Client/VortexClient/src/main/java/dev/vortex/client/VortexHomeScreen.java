package dev.vortex.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

/** Small client hub over the real world/HUD, with no blur or duplicate HUD rendering. */
public final class VortexHomeScreen extends Screen {
    private final Screen parent;
    private final long opened = System.nanoTime();
    public VortexHomeScreen(Screen parent) {
        super(Minecraft.getInstance(), UiFonts.body(), Component.literal("Vortex Client"));
        this.parent = parent;
    }
    public record Layout(int x, int y, int width, int buttonHeight, int gap) {
        public int columnWidth() { return (width - gap) / 2; }
        public int buttonX(int index) { return x + index % 2 * (columnWidth() + gap); }
        public int buttonY(int index) { return y + index / 2 * (buttonHeight + gap); }
        public int backY() { return y + 3 * (buttonHeight + gap) + 6; }
    }
    public static Layout layout(int width, int height) {
        int w = Math.min(380, Math.max(40, width - 24));
        return new Layout((width - w) / 2, Math.max(64, (height - 222) / 2 + 60), w, 32, 5);
    }
    @Override protected void init() {
        var l = layout(width, height);
        String[] labels = {"Modules", "Edit HUD", "Wardrobe", "Party", "Key bindings", "Client settings"};
        String[] icons = {"modules", "hud", "capes", "social", "keybinds", "settings"};
        Runnable[] actions = {
            () -> minecraft.gui.setScreen(new VortexScreen(this)),
            () -> minecraft.gui.setScreen(new HudEditorScreen(this)),
            () -> minecraft.gui.setScreen(new WardrobeScreen(this)),
            () -> minecraft.gui.setScreen(new PartyScreen(this)),
            () -> minecraft.gui.setScreen(new KeybindScreen(this)),
            () -> minecraft.gui.setScreen(VortexScreen.settings(this))
        };
        for (int i = 0; i < labels.length; i++) {
            final int index = i;
            addRenderableWidget(new VortexMenuChrome.ShortcutButton(l.buttonX(i), l.buttonY(i), l.columnWidth(), l.buttonHeight(),
                icons[i], labels[i], b -> actions[index].run()));
        }
        addRenderableWidget(new VortexMenuChrome.GlassButton(l.x(), l.backY(), l.width(), l.buttonHeight(), Component.literal("Back"), b -> onClose(), false));
    }
    @Override public boolean isInGameUi() { return minecraft.level != null; }
    @Override public void extractBackground(GuiGraphicsExtractor g, int mx, int my, float delta) {
        if (isInGameUi()) minecraft.gui.hud.extractDeferredSubtitles();
        else extractPanorama(g, delta);
    }
    @Override public void extractRenderState(GuiGraphicsExtractor g, int mx, int my, float delta) {
        var l = layout(width, height);
        g.fill(0, 0, width, height, 0x8506090C);
        float entrance = UiMotion.progress(opened);
        UiMotion.begin(g, l.x(), l.y() - 60, l.width(), l.backY() + l.buttonHeight() - l.y() + 60, 1, (1 - entrance) * 6);
        int y = l.y() - 60 + (int)((1 - UiMotion.progress(opened)) * 5);
        VortexMenuChrome.drawBrand(g, width / 2, y, false, "VORTEX CLIENT");
        super.extractRenderState(g, mx, my, delta);
        g.pose().popMatrix();
    }
    @Override public boolean isPauseScreen() { return false; }
    @Override public void onClose() { minecraft.gui.setScreen(parent); }
}
