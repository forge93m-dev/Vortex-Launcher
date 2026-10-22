package dev.vortex.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

/** Explicit opt-in. Clipboard contains only the invite, never the private session token. */
public final class PartyScreen extends Screen {
    private final long opened = System.nanoTime();
    private final Screen parent;
    private String url = VortexClient.config.partyServiceUrl, code = "";
    private boolean wasConnected;
    private boolean confirmClose;
    public PartyScreen(Screen parent) { super(Minecraft.getInstance(), UiFonts.body(), Component.literal("Vortex Party")); this.parent = parent; }
    public record Layout(int x, int y, int width, int height) { }
    public static Layout layout(int width, int height) {
        int w = Math.min(400, width - 16);
        return new Layout((width - w) / 2, Math.max(0, (height - 232) / 2), w, 232);
    }
    private int w() { return layout(width, height).width(); }
    private int x() { return layout(width, height).x(); }
    private int y() { return layout(width, height).y(); }
    private EditBox field(int y, String hint, String value, int max, java.util.function.Consumer<String> update) {
        var input = new EditBox(font, x() + 12, y, w() - 24, 20, Component.literal(hint));
        input.setMaxLength(max); input.setValue(value); input.setTextShadow(false); input.setResponder(update);
        addRenderableWidget(input); return input;
    }
    private void button(int x, int y, int width, String text, Runnable action, boolean primary) {
        addRenderableWidget(new ClientUi.ActionButton(x, y, width, 22, Component.literal(text), b -> action.run(), primary));
    }
    @Override protected void init() {
        wasConnected = PartyFeature.connected();
        int x = x(), y = y(), w = w();
        if (!wasConnected) {
            field(y + 48, "Party service URL", url, 240, value -> url = value);
            field(y + 88, "Invite code", code, 32, value -> code = value);
            button(x + 12, y + 119, (w - 32) / 2, "Create party", () -> connect(true), true);
            button(x + 20 + (w - 32) / 2, y + 119, (w - 32) / 2, "Join party", () -> connect(false), false);
        } else {
            button(x + 12, y + 52, w - 24, "Copy invite code", () -> minecraft.keyboardHandler.setClipboard(PartyFeature.invite()), false);
            int part = (w - 40) / 3;
            button(x + 12, y + 140, part, "I'm here", () -> PartyFeature.mark("HERE"), true);
            button(x + 20 + part, y + 140, part, "Come here", () -> PartyFeature.mark("COME"), false);
            button(x + 28 + part * 2, y + 140, part, "Danger", () -> PartyFeature.mark("DANGER"), false);
            button(x + 12, y + 172, (w - 32) / 2, confirmClose ? "Confirm close" : PartyFeature.owner() ? "Close party" : "Leave party", () -> {
                if (PartyFeature.owner() && !confirmClose) { confirmClose = true; PartyFeature.message = "Closing removes everyone. Click Confirm close, or Back to cancel."; rebuildWidgets(); }
                else { PartyFeature.leave(); confirmClose = false; rebuildWidgets(); }
            }, false);
            button(x + 20 + (w - 32) / 2, y + 172, (w - 32) / 2, "Marker key", () -> minecraft.gui.setScreen(new KeybindScreen(this, ClientBindings.Action.PARTY_MARKER)), false);
        }
        button(x + w - 64, y + 8, 52, "Back", this::onClose, false);
    }
    private void connect(boolean create) {
        try { PartyConnection.endpoint(url); }
        catch (IllegalArgumentException ex) { PartyFeature.message = ex.getMessage(); return; }
        if (PartyFeature.busy()) return;
        VortexClient.config.partyServiceUrl = url.trim();
        if (!VortexClient.config.save()) { PartyFeature.message = "Could not save service address. See game log."; return; }
        PartyFeature.connect(url, code, create);
    }
    @Override public void tick() { if (wasConnected != PartyFeature.connected()) rebuildWidgets(); }
    @Override public boolean isInGameUi() { return minecraft.level != null; }
    @Override public void extractBackground(GuiGraphicsExtractor g, int mx, int my, float delta) {
        if (isInGameUi()) minecraft.gui.hud.extractDeferredSubtitles();
        else extractPanorama(g, delta);
    }
    @Override public void extractRenderState(GuiGraphicsExtractor g, int mx, int my, float delta) {
        int x = x(), y = y(), w = w();
        ClientUi.beginPage(g, width, height, x, y, w, 232, "Vortex Party", "social", opened);
        if (!PartyFeature.connected()) {
            ClientUi.text(g, "Service URL (HTTPS)", x + 12, y + 37, w - 24, ClientUi.MUTED);
            ClientUi.text(g, "Invite code (only needed to join)", x + 12, y + 74, w - 24, ClientUi.MUTED);
            ClientUi.text(g, "Only manual markers share coordinates.", x + 12, y + 157, w - 24, ClientUi.MUTED);
            ClientUi.text(g, "No public chat messages. No player tracking.", x + 12, y + 171, w - 24, ClientUi.MUTED);
            ClientUi.text(g, "Your service must be hosted before online use.", x + 12, y + 185, w - 24, ClientUi.MUTED);
        } else {
            var members = PartyFeature.members();
            ClientUi.text(g, "Friends: " + members.size() + "/8 · Local names, not verified accounts", x + 12, y + 38, w - 24, ClientUi.MUTED);
            for (int i = 0; i < members.size(); i++) {
                var member = members.get(i);
                ClientUi.text(g, member.name() + (member.owner() ? " *" : ""), x + 12 + i % 2 * ((w - 24) / 2), y + 76 + i / 2 * 14, (w - 32) / 2, ClientUi.TEXT);
            }
        }
        ClientUi.text(g, PartyFeature.busy() ? "Connecting..." : PartyFeature.message, x + 12, y + 214, w - 24, ClientUi.ACCENT);
        super.extractRenderState(g, mx, my, delta);
        g.pose().popMatrix();
    }
    @Override public boolean isPauseScreen() { return false; }
    @Override public void onClose() { minecraft.gui.setScreen(parent); }
}
