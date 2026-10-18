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
        int w = Math.min(300, Math.max(40, width - 24));
        return new Layout((width - w) / 2, Math.max(64, (height - 180) / 2 + 60), w, 22, 5);
    }
    @Override protected void init() {
        var l = layout(width, height);
        int w = Math.min(220, l.width()), x = (width - w) / 2;
        addRenderableWidget(new VortexMenuChrome.GlassButton(x, l.y(), w, 26, Component.literal("Modules"),
            b -> minecraft.gui.setScreen(new VortexScreen(this)), false));
        int half = (w - 4) / 2;
        addRenderableWidget(new VortexMenuChrome.GlassButton(x, l.y() + 30, half, 22, Component.literal("Edit HUD"),
            b -> minecraft.gui.setScreen(new HudEditorScreen(this)), false));
        addRenderableWidget(new VortexMenuChrome.GlassButton(x + half + 4, l.y() + 30, half, 22, Component.literal("Wardrobe"),
            b -> minecraft.gui.setScreen(new WardrobeScreen(this)), false));
        int dockX = (width - 108) / 2, dockY = height - 44;
        addRenderableWidget(new VortexMenuChrome.DockButton(dockX, dockY, "social", "Party", b -> minecraft.gui.setScreen(new PartyScreen(this))));
        addRenderableWidget(new VortexMenuChrome.DockButton(dockX + 28, dockY, "keybinds", "Key bindings", b -> minecraft.gui.setScreen(new KeybindScreen(this))));
        addRenderableWidget(new VortexMenuChrome.DockButton(dockX + 56, dockY, "settings", "Client settings", b -> minecraft.gui.setScreen(VortexScreen.settings(this))));
        addRenderableWidget(new VortexMenuChrome.DockButton(dockX + 84, dockY, "back", "Back", b -> onClose()));
    }
    @Override public boolean isInGameUi() { return minecraft.level != null; }
    @Override public void extractBackground(GuiGraphicsExtractor g, int mx, int my, float delta) {
        if (isInGameUi()) minecraft.gui.hud.extractDeferredSubtitles();
        else extractPanorama(g, delta);
    }
    @Override public void extractRenderState(GuiGraphicsExtractor g, int mx, int my, float delta) {
        var l = layout(width, height);
        g.fill(0, 0, width, height, 0x1806090C);
        int y = l.y() - 60 + (int)((1 - UiMotion.progress(opened)) * 5);
        VortexMenuChrome.drawBrand(g, width / 2, y, false, "VORTEX CLIENT");
        super.extractRenderState(g, mx, my, delta);
    }
    @Override public boolean isPauseScreen() { return false; }
    @Override public void onClose() { minecraft.gui.setScreen(parent); }
}
