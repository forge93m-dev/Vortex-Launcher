package dev.vortex.client;

import java.util.List;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

/** Places the party shares: save a copy to your own waypoints, or remove your own (the owner may remove any). */
public final class PartyWaypointsScreen extends Screen {
    private final long opened = System.nanoTime();
    private final Screen parent;
    private int scroll, shownCount = -1;
    private String note = "";
    public PartyWaypointsScreen(Screen parent) { super(Minecraft.getInstance(), UiFonts.body(), Component.literal("Shared waypoints")); this.parent = parent; }
    public record Layout(int x, int y, int width, int height) {
        public int rows() { return Math.max(1, (height - 96) / 44); }
        public int listY() { return y + 56; }
    }
    public static Layout layout(int width, int height) {
        int w = Math.min(460, width - 16), h = Math.min(320, height - 16);
        return new Layout((width - w) / 2, (height - h) / 2, w, h);
    }
    private void button(int x, int y, int w, String text, Runnable action, boolean primary, boolean enabled) {
        var b = new ClientUi.ActionButton(x, y, w, 22, Component.literal(text), ignored -> action.run(), primary);
        b.active = enabled; addRenderableWidget(b);
    }
    @Override protected void init() {
        var l = layout(width, height); int x = l.x() + 16, w = l.width() - 32;
        var places = PartyFeature.sharedPlaces(); shownCount = places.size();
        scroll = Math.clamp(scroll, 0, Math.max(0, places.size() - l.rows()));
        button(x + w - 52, l.y() + 10, 52, "Done", this::onClose, false, true);
        String me = PartyFeature.memberId();
        for (int i = 0; i < l.rows() && scroll + i < places.size(); i++) {
            var place = places.get(scroll + i);
            int rowY = l.listY() + i * 44;
            boolean mine = place.source().memberId().equals(me);
            button(x + w - 128, rowY + 9, 60, "Save", () -> save(place), false, WaypointsFeature.scope() != null);
            button(x + w - 62, rowY + 9, 62, "Remove", () -> PartyFeature.unshare(place.source().id()), false, mine || PartyFeature.owner());
        }
    }
    /** Copies a shared place into this world's own waypoints; the copy is private and can be edited freely. */
    private void save(PartyFeature.SharedPlace place) {
        var scope = WaypointsFeature.scope(); var source = place.source();
        if (scope == null || !scope.dimension().equals(source.dimension())) { note = "Join that server and dimension to save this place."; return; }
        var blocks = source.blocks() == null ? List.<WaypointsStore.Block>of() : source.blocks().stream().map(b -> new WaypointsStore.Block(b.x(), b.y(), b.z())).toList();
        var copy = new WaypointsStore.Point(java.util.UUID.randomUUID(), scope.world(), scope.dimension(), WaypointsStore.cleanName(source.title()),
            source.x(), source.y(), source.z(), source.color(), false, "Party", blocks);
        if (!WaypointsStore.valid(copy)) { note = "This place cannot be saved here."; return; }
        WaypointsFeature.save(copy, null, ok -> note = ok ? "Saved to your waypoints (group Party)." : WaypointsFeature.message);
    }
    @Override public void tick() { if (shownCount != PartyFeature.sharedPlaces().size()) rebuildWidgets(); }
    @Override public boolean mouseScrolled(double mx, double my, double horizontal, double vertical) {
        var l = layout(width, height);
        int max = Math.max(0, PartyFeature.sharedPlaces().size() - l.rows());
        if (Double.isFinite(vertical) && vertical != 0 && max > 0) { scroll = Math.clamp(scroll - (int)Math.signum(vertical), 0, max); rebuildWidgets(); return true; }
        return super.mouseScrolled(mx, my, horizontal, vertical);
    }
    @Override public void extractRenderState(GuiGraphicsExtractor g, int mx, int my, float delta) {
        var l = layout(width, height); int x = l.x() + 16, w = l.width() - 32;
        var places = PartyFeature.sharedPlaces();
        WaypointUi.begin(g, width, height, l.x(), l.y(), l.width(), l.height(), "Shared waypoints", places.size() + " of 32 · visible to everyone in your party", opened);
        if (places.isEmpty()) {
            ClientUi.text(g, "Nothing shared yet.", x, l.listY() + 8, w, ClientUi.TEXT);
            ClientUi.text(g, "Open a saved waypoint and press Share.", x, l.listY() + 24, w, ClientUi.MUTED);
        }
        for (int i = 0; i < l.rows() && scroll + i < places.size(); i++) {
            var source = places.get(scroll + i).source();
            int rowY = l.listY() + i * 44;
            ClientUi.rounded(g, x, rowY, w, 40, 12, ClientUi.SURFACE);
            ClientUi.rounded(g, x + 8, rowY + 14, 12, 12, 6, 0xFF000000 | source.color());
            ClientUi.text(g, source.title() + " · " + source.name(), x + 28, rowY + 7, w - 170, ClientUi.TEXT);
            String blocks = source.blocks() == null || source.blocks().isEmpty() ? "" : " · " + source.blocks().size() + " blocks";
            ClientUi.text(g, source.x() + ", " + source.y() + ", " + source.z() + blocks, x + 28, rowY + 23, w - 170, ClientUi.MUTED);
        }
        ClientUi.text(g, note.isEmpty() ? PartyFeature.message : note, x, l.y() + l.height() - 16, w, ClientUi.MUTED);
        super.extractRenderState(g, mx, my, delta);
        g.pose().popMatrix();
    }
    @Override public boolean isPauseScreen() { return false; }
    @Override public boolean isInGameUi() { return minecraft.level != null; }
    @Override public void extractBackground(GuiGraphicsExtractor g, int mx, int my, float delta) {
        if (minecraft.level != null) minecraft.gui.hud.extractDeferredSubtitles(); else extractPanorama(g, delta);
    }
    @Override public void onClose() { minecraft.gui.setScreen(parent); }
}
