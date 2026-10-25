package dev.vortex.client;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

/** Picks the title-screen panorama: the current one or one from an older Minecraft version (downloaded once). */
public final class PanoramaScreen extends Screen {
    private final long opened = System.nanoTime();
    private final Screen parent;
    private boolean wasBusy, wasLooking;
    private int scroll, listSize;
    public PanoramaScreen(Screen parent) { super(Minecraft.getInstance(), UiFonts.body(), Component.literal("Menu panorama")); this.parent = parent; }
    /** Rows fill column by column; when even three columns are not enough, the rows scroll. */
    /** Row pitch: two-line rows (name, then version) 32 px tall with a 4 px gap. */
    static final int ROW = 36;
    public record Layout(int x, int y, int width, int height, int columns, int rows, int visibleRows) {
        public int itemWidth() { return (width - 32 - (columns - 1) * 8) / columns; }
        public int itemX(int index) { return x + 16 + index % columns * (itemWidth() + 8); }
        public int itemY(int index) { return y + 56 + index / columns * ROW; }
        public int maxScroll() { return Math.max(0, rows - visibleRows); }
    }
    public static Layout layout(int screenWidth, int screenHeight, int items) {
        int w = Math.min(560, screenWidth - 16), h = Math.min(340, screenHeight - 16);
        int visible = Math.max(1, (h - 56 - 34) / ROW);
        int columns = Math.max(1, Math.min(w >= 420 ? 3 : 2, (items + visible - 1) / visible));
        int rows = (items + columns - 1) / columns;
        return new Layout((screenWidth - w) / 2, (screenHeight - h) / 2, w, h, columns, rows, Math.min(visible, rows));
    }
    private static List<String> versions() {
        var result = new ArrayList<String>(); result.add("");
        for (var choice : PanoramaLibrary.choices()) result.add(choice.version());
        return result;
    }
    @Override protected void init() {
        wasBusy = PanoramaLibrary.busy(); wasLooking = PanoramaLibrary.looking();
        PanoramaLibrary.refreshList();
        var versions = versions(); listSize = versions.size();
        var l = layout(width, height, versions.size());
        scroll = Math.clamp(scroll, 0, l.maxScroll());
        addRenderableWidget(new ClientUi.ActionButton(l.x() + l.width() - 68, l.y() + 10, 52, 22, Component.literal("Done"), b -> onClose()));
        int first = scroll * l.columns(), last = Math.min(versions.size(), first + l.visibleRows() * l.columns());
        for (int i = first; i < last; i++) {
            String version = versions.get(i);
            var row = new Row(l.itemX(i - first), l.itemY(i - first), l.itemWidth(), version);
            row.active = !wasBusy;
            addRenderableWidget(row);
        }
    }
    @Override public void tick() {
        if (wasBusy != PanoramaLibrary.busy() || wasLooking != PanoramaLibrary.looking() || listSize != versions().size()) rebuildWidgets();
    }
    @Override public boolean mouseScrolled(double mx, double my, double horizontal, double vertical) {
        var l = layout(width, height, listSize);
        if (Double.isFinite(vertical) && vertical != 0 && l.maxScroll() > 0) {
            scroll = Math.clamp(scroll - (int)Math.signum(vertical), 0, l.maxScroll()); rebuildWidgets(); return true;
        }
        return super.mouseScrolled(mx, my, horizontal, vertical);
    }
    @Override public void extractRenderState(GuiGraphicsExtractor g, int mx, int my, float delta) {
        var l = layout(width, height, listSize);
        WaypointUi.begin(g, width, height, l.x(), l.y(), l.width(), l.height(), "Menu panorama",
            "The main menu background from any Minecraft version", opened);
        String footer = PanoramaLibrary.busy() ? PanoramaLibrary.status() + "  " + Math.max(0, PanoramaLibrary.progress()) + "%"
            : PanoramaLibrary.looking() ? "Looking up every panorama in Mojang's version list..."
            : !PanoramaLibrary.status().isEmpty() ? PanoramaLibrary.status() : "Each panorama is downloaded once from Mojang (about 3-8 MB)." + (l.maxScroll() > 0 ? " Scroll for more." : "");
        ClientUi.text(g, footer, l.x() + 16, l.y() + l.height() - 22, l.width() - 32, ClientUi.MUTED);
        if (PanoramaLibrary.busy()) {
            int barX = l.x() + 16, barW = l.width() - 32, barY = l.y() + l.height() - 30;
            ClientUi.rounded(g, barX, barY, barW, 3, 1, ClientUi.TRACK);
            ClientUi.rounded(g, barX, barY, Math.max(3, barW * Math.max(0, PanoramaLibrary.progress()) / 100), 3, 1, ClientUi.ACCENT);
        }
        super.extractRenderState(g, mx, my, delta);
        g.pose().popMatrix();
    }
    @Override public boolean isPauseScreen() { return false; }
    @Override public boolean isInGameUi() { return minecraft.level != null; }
    @Override public void extractBackground(GuiGraphicsExtractor g, int mx, int my, float delta) {
        // From the main menu the real panorama shows behind the list, so a choice is visible at once.
        if (minecraft.level != null) minecraft.gui.hud.extractDeferredSubtitles(); else extractPanorama(g, delta);
    }
    @Override public void onClose() { minecraft.gui.setScreen(parent); }

    /** One choice: name, version and whether it is chosen, downloaded or still to download. */
    private static final class Row extends ClientUi.ActionButton {
        private final String version;
        private final boolean downloaded;
        private final UiMotion.Feedback motion = new UiMotion.Feedback();
        Row(int x, int y, int w, String version) {
            super(x, y, w, 32, Component.literal(PanoramaLibrary.title(version)), b -> PanoramaLibrary.choose(version));
            this.version = version;
            // Checked once per rebuild (after each download), not every frame.
            downloaded = version.isEmpty() || PanoramaLibrary.installed(PanoramaLibrary.folder(), version);
        }
        @Override protected void extractContents(GuiGraphicsExtractor g, int mx, int my, float delta) {
            boolean chosen = version.equals(VortexClient.config.menuPanorama);
            motion.frame(active && isHoveredOrFocused(), chosen);
            UiMotion.begin(g, getX(), getY(), getWidth(), getHeight(), pressScale(System.nanoTime()), 0);
            int background = ClientUi.mix(ClientUi.mix(ClientUi.SURFACE, ClientUi.RAISED, motion.hover), ClientUi.ACCENT_TINT, motion.selected);
            ClientUi.pill(g, getX(), getY(), getWidth(), getHeight(), background);
            if (chosen) ClientUi.rounded(g, getX() + 9, getY() + 13, 6, 6, 3, ClientUi.ACCENT);
            var choice = PanoramaLibrary.find(version);
            String name = choice == null ? "Current Minecraft" : choice.title();
            // Narrow columns (very small windows) show the name only.
            // Second line: the version, and whether it still has to be downloaded.
            String detail = choice == null ? "The panorama of this game" : downloaded ? version : version + " · download";
            var font = UiFonts.body();
            ClientUi.text(g, name, getX() + 22, getY() + 5, getWidth() - 30, active ? ClientUi.TEXT : ClientUi.MUTED);
            ClientUi.text(g, detail, getX() + 22, getY() + 17, getWidth() - 30, ClientUi.MUTED);
            g.pose().popMatrix();
        }
    }
}
