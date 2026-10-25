package dev.vortex.client;

import java.util.ArrayList;
import java.util.List;
import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.ChatComponent;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.network.chat.Component;

/** Tabs, search and the right-click menu drawn on the open chat box (ChatScreenMixin). */
public final class ChatOverlay {
    private ChatOverlay() { }
    static final int TAB_HEIGHT = 16, MENU_WIDTH = 124, MENU_ROW = 18;
    static final String[] MENU = {"Copy text", "Copy name", "Reply privately"};
    private static Component menuTarget;
    private static EditBox search;
    private static int menuX, menuY;

    /** Chat box geometry in GUI pixels: {right edge, top edge}; vanilla draws from 40 px above the bottom. */
    public static int[] chatBox(int screenWidth, int screenHeight, double widthOption, double scale, double spacing, int linesPerPage) {
        int right = (int)Math.ceil((ChatComponent.getWidth(widthOption) + 8) * scale);
        int top = screenHeight - 40 - (int)Math.ceil(linesPerPage * 9 * (spacing + 1) * scale);
        return new int[] {Math.min(screenWidth - 2, right), top};
    }
    /** Visible row under the mouse, 0 = bottom row; -1 outside the chat lines. */
    public static int rowAt(double mx, double my, int screenHeight, double widthOption, double scale, double spacing, int linesPerPage) {
        if (!(scale > 0)) return -1;
        double lineHeight = 9 * (spacing + 1), localX = mx / scale, localY = my / scale, bottom = Math.floor((screenHeight - 40) / scale);
        if (localX < 0 || localX > ChatComponent.getWidth(widthOption) + 8 || localY > bottom) return -1;
        int row = (int)Math.floor((bottom - localY) / lineHeight);
        return row < linesPerPage ? row : -1;
    }
    private static int[] box(Minecraft mc, int width, int height) {
        var o = mc.options;
        return chatBox(width, height, o.chatWidth().get(), o.chatScale().get(), o.chatLineSpacing().get(), mc.gui.hud.getChat().getLinesPerPage());
    }
    public static List<AbstractWidget> widgets(int width, int height) {
        var mc = Minecraft.getInstance();
        menuTarget = null;
        ChatTabs.reopen();
        int[] box = box(mc, width, height);
        int y = Math.max(2, box[1] - TAB_HEIGHT - 4), x = 2;
        var result = new ArrayList<AbstractWidget>();
        String[] labels = {"All", "Private", "Party"};
        for (var tab : ChatTabs.Tab.values()) {
            int w = UiFonts.body().width(labels[tab.ordinal()]) + 20;
            result.add(new TabButton(x, y, w, tab, labels[tab.ordinal()]));
            x += w + 3;
        }
        int searchWidth = Math.min(130, Math.max(70, box[0] - x - 8));
        search = new EditBox(mc.font, Math.max(x + 6, box[0] - searchWidth), y + 3, searchWidth - 6, 12, Component.literal("Search chat"));
        search.setBordered(false); search.setTextShadow(false); search.setMaxLength(64);
        search.setHint(Component.literal("Search chat").withColor(ClientUi.MUTED));
        search.setValue(ChatTabs.query());
        search.setResponder(ChatTabs::search);
        result.add(search);
        return result;
    }
    public static void render(GuiGraphicsExtractor g, int width, int height, int mx, int my) {
        var mc = Minecraft.getInstance();
        int[] box = box(mc, width, height);
        int y = Math.max(2, box[1] - TAB_HEIGHT - 4);
        if (!ChatTabs.query().isEmpty() && mc.gui.hud.getChat() instanceof ChatAccess access) {
            String found = access.vortex$matchingCount() + " found";
            g.text(UiFonts.body(), found, box[0] + 4, y + 4, ClientUi.MUTED, false);
        }
        if (ChatTabs.tab() == ChatTabs.Tab.PARTY) {
            String hint = PartyFeature.connected() ? "Writing to your party · Tab: all chat" : "Join a party to write here · Tab: all chat";
            int w = UiFonts.body().width(hint) + 14;
            ClientUi.pill(g, width - w - 4, height - 30, w, 14, (ClientUi.ACCENT_TINT & 0xFFFFFF | 0xE0000000));
            g.text(UiFonts.body(), hint, width - w + 3, height - 27, ClientUi.ACCENT_PALE, false);
        }
        if (menuTarget != null) {
            int h = MENU.length * MENU_ROW + 6;
            ClientUi.rounded(g, menuX, menuY, MENU_WIDTH, h, 8, ClientUi.SURFACE);
            boolean named = !ChatTabs.name(menuTarget).isEmpty();
            for (int i = 0; i < MENU.length; i++) {
                int rowY = menuY + 3 + i * MENU_ROW;
                boolean enabled = i == 0 || named;
                if (enabled && mx >= menuX && mx < menuX + MENU_WIDTH && my >= rowY && my < rowY + MENU_ROW)
                    ClientUi.rounded(g, menuX + 3, rowY, MENU_WIDTH - 6, MENU_ROW, 6, ClientUi.RAISED);
                g.text(UiFonts.body(), MENU[i], menuX + 10, rowY + 5, enabled ? ClientUi.TEXT : ClientUi.MUTED, false);
            }
        }
    }
    /** Right-click on a chat line opens the menu; a click on a menu row runs it. True when the click was used. */
    public static boolean mouseClicked(double mx, double my, int button, int width, int height, EditBox input) {
        var mc = Minecraft.getInstance();
        if (menuTarget != null) {
            var target = menuTarget; menuTarget = null;
            int row = (int)Math.floor((my - menuY - 3) / MENU_ROW);
            if (mx < menuX || mx >= menuX + MENU_WIDTH || row < 0 || row >= MENU.length) return my >= menuY && my < menuY + MENU.length * MENU_ROW + 6 && mx >= menuX && mx < menuX + MENU_WIDTH;
            String name = ChatTabs.name(target);
            switch (row) {
                case 0 -> mc.keyboardHandler.setClipboard(target.getString());
                case 1 -> { if (!name.isEmpty()) mc.keyboardHandler.setClipboard(name); }
                default -> { if (!name.isEmpty() && input != null) { input.setValue("/msg " + name + " "); input.setFocused(true); } }
            }
            return true;
        }
        if (button != InputConstants.MOUSE_BUTTON_RIGHT || !(mc.gui.hud.getChat() instanceof ChatAccess access)) return false;
        var o = mc.options;
        int row = rowAt(mx, my, height, o.chatWidth().get(), o.chatScale().get(), o.chatLineSpacing().get(), mc.gui.hud.getChat().getLinesPerPage());
        var target = row < 0 ? null : access.vortex$messageAtRow(row);
        if (target == null) return false;
        menuTarget = target;
        menuX = (int)Math.min(mx, width - MENU_WIDTH - 2);
        menuY = (int)Math.max(2, Math.min(my, height - MENU.length * MENU_ROW - 10));
        return true;
    }
    /**
     * Where typing should go after a click or key: tabs never keep keyboard focus (you could not type until
     * clicking the chat line again), and Enter in the search box returns to the message line.
     */
    public static boolean returnsFocus(Object focused, int key) {
        if (focused instanceof TabButton) return true;
        return focused == search && search != null && (key == InputConstants.KEY_RETURN || key == InputConstants.KEY_NUMPADENTER);
    }
    /** Tab on an empty line switches between all chat and the party; otherwise vanilla completion runs. */
    public static boolean keyPressed(int key, EditBox input) {
        if (key == InputConstants.KEY_ESCAPE && menuTarget != null) { menuTarget = null; return true; }
        if (key != InputConstants.KEY_TAB || input == null || !input.isFocused() || !input.getValue().isEmpty()) return false;
        ChatTabs.select(ChatTabs.tab() == ChatTabs.Tab.PARTY ? ChatTabs.Tab.ALL : ChatTabs.Tab.PARTY);
        return true;
    }
    static final class TabButton extends ClientUi.ActionButton {
        private final ChatTabs.Tab tab;
        private final UiMotion.Feedback motion = new UiMotion.Feedback();
        TabButton(int x, int y, int w, ChatTabs.Tab tab, String label) {
            super(x, y, w, TAB_HEIGHT, Component.literal(label), b -> ChatTabs.select(tab));
            this.tab = tab;
        }
        @Override protected void extractContents(GuiGraphicsExtractor g, int mx, int my, float delta) {
            boolean chosen = ChatTabs.tab() == tab;
            motion.frame(isHoveredOrFocused(), chosen);
            int background = ClientUi.mix(ClientUi.mix(0xC015161C, 0xE02B2E3A, motion.hover), (ClientUi.ACCENT_TINT & 0xFFFFFF | 0xF0000000), motion.selected);
            ClientUi.pill(g, getX(), getY(), getWidth(), getHeight(), background);
            var font = UiFonts.body();
            String label = getMessage().getString();
            g.text(font, label, getX() + (getWidth() - font.width(label)) / 2, getY() + 4, ClientUi.mix(ClientUi.MUTED, ClientUi.TEXT, Math.max(motion.hover, motion.selected)), false);
            if (ChatTabs.unread(tab)) ClientUi.rounded(g, getX() + getWidth() - 7, getY() + 2, 5, 5, 2, tab == ChatTabs.Tab.PRIVATE ? 0xFFFB7185 : ClientUi.ACCENT);
        }
    }
}
