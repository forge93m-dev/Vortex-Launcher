package dev.vortex.client;

import java.util.ArrayList;
import java.util.List;
import java.util.Comparator;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.InputWithModifiers;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.contents.TranslatableContents;

/** Only TitleScreen and normal PauseScreen use this chrome. Vanilla actions stay authoritative. */
public final class VortexMenuChrome {
    /**
     * A left-hand column: brand, large primary actions, the Modules/Wardrobe pair, a grid of the rest,
     * then one row of small round icons for rarely used vanilla actions (Language, Accessibility, ...).
     */
    public record Layout(int x, int y, int width, int height, int columns, int rows, int buttonWidth, int buttonHeight,
                         int gap, int header, int primaryCount, int utilityHeight, int utilityY, int count, int icons) {
        public int utilityWidth() { return (width - (columns - 1) * gap) / columns; }
        /** The last utility action fills the rest of its row, so an odd count never leaves a hole. */
        public int widthFor(int index) {
            if (index < primaryCount) return buttonWidth;
            return index == count - 1 ? x + width - buttonX(index) : utilityWidth();
        }
        public int heightFor(int index) { return index < primaryCount ? buttonHeight : utilityHeight; }
        public int buttonX(int index) { return index < primaryCount ? x + (width - buttonWidth) / 2 : x + (index - primaryCount) % columns * (utilityWidth() + gap); }
        public int buttonY(int index) { return index < primaryCount ? y + header + index * (buttonHeight + gap) : y + utilityY + (index - primaryCount) / columns * (utilityHeight + gap); }
        public int pairY() { return y + header + primaryCount * (buttonHeight + gap); }
        public int iconY() { return y + utilityY + rows * (utilityHeight + gap) + 2; }
        public int iconX(int index) { return x + index * (ICON_SIZE + 6); }
        public int contentBottom() { return icons > 0 ? iconY() + ICON_SIZE : y + utilityY + rows * (utilityHeight + gap) - gap; }
    }
    static final int ICON_SIZE = 24;
    /** Vanilla actions shown as round icons with tooltips instead of full-width buttons. */
    public static boolean iconAction(String key) {
        return switch (key) {
            case "options.language", "options.accessibility", "narrator.button.accessibility",
                 "menu.sendFeedback", "menu.reportBugs", "menu.playerReporting" -> true;
            default -> false;
        };
    }
    /** The copyright line opens the credits; it stays a text link in the footer, as in vanilla. */
    public static boolean linkAction(String key) { return key.equals("title.credits"); }
    static final int PAIR_HEIGHT = 22;
    private static final int CARD_WIDTH = 168;
    /** Title-screen dock: client tools not already in the menu column. */
    static final String[][] DOCK_ITEMS = {{"hud", "HUD Editor"}, {"social", "Party"}, {"keybinds", "Key bindings"}, {"settings", "Settings"}};
    static final int DOCK_ITEM_HEIGHT = 22, DOCK_PADDING = 4, DOCK_GAP = 4;
    /** Bottom-centre dock geometry; null when it would crowd the menu column or the footer. */
    public record Dock(int x, int y, int width, int height, int[] itemWidths) {
        public int itemX(int index) {
            int position = x + DOCK_PADDING;
            for (int i = 0; i < index; i++) position += itemWidths[i] + DOCK_GAP;
            return position;
        }
        public int itemY() { return y + DOCK_PADDING; }
    }
    /** Label widths are estimated so the layout can be computed (and tested) without a font. */
    static int dockItemWidth(String label) { return 30 + label.length() * 6; }
    public static Dock dock(int screenWidth, int screenHeight, Layout column) {
        int[] widths = new int[DOCK_ITEMS.length];
        int width = 2 * DOCK_PADDING + (DOCK_ITEMS.length - 1) * DOCK_GAP;
        for (int i = 0; i < DOCK_ITEMS.length; i++) width += widths[i] = dockItemWidth(DOCK_ITEMS[i][1]);
        int height = DOCK_ITEM_HEIGHT + 2 * DOCK_PADDING;
        // The footer text sits at screenHeight - 14; keep a clear line above it.
        int x = (screenWidth - width) / 2, y = screenHeight - 20 - height;
        if (x < 4 || y < 4) return null;
        int columnRight = column.x() + column.width() + 6, columnBottom = column.contentBottom() + 6;
        if (x < columnRight && y < columnBottom) return null;
        return new Dock(x, y, width, height, widths);
    }
    private final Screen owner;
    private final boolean pause;
    private final List<Button> sources;
    private final long opened = System.nanoTime();
    private final List<MenuButton> wrapped = new ArrayList<>();
    private Layout layout;
    private Dock dock;
    private String worldName = "";
    private int cardsX = -1, cardsTop;

    public VortexMenuChrome(Screen owner, boolean pause, List<Button> sources) {
        this.owner = owner; this.pause = pause;
        this.sources = sources.stream().sorted(Comparator.comparingInt(b -> priority(key(b.getMessage())))).toList();
    }
    private static int priority(String key) {
        return switch (key) {
            case "menu.singleplayer", "menu.playdemo", "menu.returnToGame" -> 0;
            case "menu.multiplayer" -> 1;
            case "menu.online", "gui.advancements" -> 2;
            case "gui.stats" -> 3;
            case "menu.options" -> 4;
            case "menu.shareToLan" -> 5;
            case "options.language" -> 6;
            case "options.accessibility", "narrator.button.accessibility" -> 7;
            case "title.credits" -> 8;
            case "menu.quit", "menu.returnToMenu", "menu.disconnect" -> 9;
            default -> 6;
        };
    }
    public static Layout layout(int screenWidth, int screenHeight, int buttonCount) {
        return layout(screenWidth, screenHeight, buttonCount, false);
    }
    public static Layout layout(int screenWidth, int screenHeight, int buttonCount, boolean pause) {
        return layout(screenWidth, screenHeight, buttonCount, pause, 0);
    }
    /** {@code icons} may come back smaller than requested when the column is too narrow for all of them. */
    public static Layout layout(int screenWidth, int screenHeight, int buttonCount, boolean pause, int iconCount) {
        int count = Math.max(1, buttonCount);
        boolean small = screenHeight < 300;
        int primaryCount = Math.min(count, pause ? 1 : 2);
        int header = small ? 38 : 64, gap = small ? 2 : 5;
        int buttonHeight = small ? 20 : 24, utilityHeight = small ? 20 : 22;
        int margin = Math.max(6, Math.min(36, screenWidth / 24));
        int maxWidth = Math.max(40, screenWidth - 2 * margin);
        int w = Math.min(maxWidth, 230);
        int utilityCount = count - primaryCount;
        int columns = Math.max(1, Math.min(2, utilityCount));
        int top = header + primaryCount * (buttonHeight + gap) + PAIR_HEIGHT + 6;
        int icons = Math.max(0, Math.min(iconCount, (w + 6) / (ICON_SIZE + 6)));
        int iconBand = icons > 0 ? ICON_SIZE + 2 : 0;
        int available = screenHeight - 22 - iconBand;
        int rows;
        // Prefer more labeled columns, then a wider column, before anything leaves the window.
        while (true) {
            rows = (utilityCount + columns - 1) / columns;
            int total = top + rows * (utilityHeight + gap);
            if (total + 4 <= available) break;
            if (columns < utilityCount && (w - columns * gap) / (columns + 1) >= 64) columns++;
            else if (w < maxWidth) w = Math.min(maxWidth, w + 24);
            else break;
        }
        int total = top + rows * (utilityHeight + gap) + iconBand;
        int y = Math.max(4, Math.min((screenHeight - total) / 2, screenHeight - 18 - total));
        return new Layout(margin, y, w, screenHeight - y - 4, columns, rows,
            w, buttonHeight, gap, header, primaryCount, utilityHeight, top, count, icons);
    }
    public List<Button> widgets(int width, int height) {
        var column = new ArrayList<Button>();
        var icons = new ArrayList<Button>();
        Button credits = null;
        for (var source : sources) {
            String key = key(source.getMessage());
            if (iconAction(key)) icons.add(source);
            else if (linkAction(key) && !pause) credits = source;
            else column.add(source);
        }
        layout = layout(width, height, column.size(), pause, icons.size());
        if (layout.icons() < icons.size()) {
            // Too narrow for every icon: the rest become labeled shortcuts so nothing is lost.
            column.addAll(column.size() - (column.isEmpty() ? 0 : 1), icons.subList(layout.icons(), icons.size()));
            icons = new ArrayList<>(icons.subList(0, layout.icons()));
            layout = layout(width, height, column.size(), pause, icons.size());
        }
        wrapped.clear();
        var result = new ArrayList<Button>();
        for (int slot = 0; slot < column.size(); slot++) {
            var source = column.get(slot);
            String key = key(source.getMessage());
            boolean primary = key.equals("menu.singleplayer") || key.equals("menu.playdemo") || key.equals("menu.returnToGame");
            var button = new MenuButton(source, layout.buttonX(slot), layout.buttonY(slot), layout.widthFor(slot), layout.heightFor(slot), primary,
                slot >= layout.primaryCount() ? MenuButton.SHORTCUT : MenuButton.PILL);
            button.revealDelay(slot);
            wrapped.add(button); result.add(button);
        }
        for (int i = 0; i < icons.size(); i++) {
            var button = new MenuButton(icons.get(i), layout.iconX(i), layout.iconY(), ICON_SIZE, ICON_SIZE, false, MenuButton.ICON);
            button.revealDelay(column.size() + i);
            wrapped.add(button); result.add(button);
        }
        if (credits != null) {
            var font = UiFonts.body();
            String text = credits.getMessage().getString();
            int linkWidth = Math.min(width / 2 - 8, font.width(text) + 4);
            var link = new MenuButton(credits, 8, height - 17, linkWidth, 12, false, MenuButton.LINK);
            wrapped.add(link); result.add(link);
        }
        int pairWidth = (layout.buttonWidth() - layout.gap()) / 2;
        var modules = new GlassButton(layout.x(), layout.pairY(), pairWidth, PAIR_HEIGHT, Component.literal("Modules"),
            b -> Minecraft.getInstance().gui.setScreen(new VortexScreen(owner)), false);
        var wardrobe = new GlassButton(layout.x() + pairWidth + layout.gap(), layout.pairY(), pairWidth, PAIR_HEIGHT, Component.literal("Wardrobe"),
            b -> Minecraft.getInstance().gui.setScreen(new WardrobeScreen(owner)), false);
        modules.accent(); wardrobe.accent();
        modules.revealDelay(layout.primaryCount()); wardrobe.revealDelay(layout.primaryCount());
        result.add(modules); result.add(wardrobe);
        if (!pause) {
            var minecraft = Minecraft.getInstance();
            String name = minecraft == null || minecraft.getUser() == null ? "Player" : minecraft.getUser().getName();
            int[] chip = chip(width, layout, name);
            if (chip != null) {
                var profile = new PlayerChip(chip[0], chip[1], chip[2], name, b -> Minecraft.getInstance().gui.setScreen(new WardrobeScreen(owner)));
                profile.revealDelay(2);
                result.add(profile);
            }
        }
        dock = pause ? null : dock(width, height, layout);
        if (dock != null) {
            Runnable[] actions = {
                () -> Minecraft.getInstance().gui.setScreen(new HudEditorScreen(owner)),
                () -> Minecraft.getInstance().gui.setScreen(new PartyScreen(owner)),
                () -> Minecraft.getInstance().gui.setScreen(new KeybindScreen(owner)),
                () -> Minecraft.getInstance().gui.setScreen(VortexScreen.settings(owner))
            };
            for (int i = 0; i < DOCK_ITEMS.length; i++) {
                final int index = i;
                var item = new ShortcutButton(dock.itemX(i), dock.itemY(), dock.itemWidths()[i], DOCK_ITEM_HEIGHT,
                    DOCK_ITEMS[i][0], DOCK_ITEMS[i][1], b -> actions[index].run());
                // The dock rises last, after the menu column.
                item.revealDelay(8 + i);
                result.add(item);
            }
        }
        cardsX = -1;
        var minecraft = Minecraft.getInstance();
        worldName = pause ? worldName(minecraft) : "";
        if (pause && minecraft != null && minecraft.player != null) {
            int margin = layout.x();
            int x = width - margin - CARD_WIDTH;
            // Side cards appear only where they cannot crowd the menu column.
            if (x >= layout.x() + layout.width() + 16 && height >= 230) {
                cardsTop = Math.max(8, Math.min(layout.y(), height - 22 - 210));
                cardsX = x;
                ClientModule[] quick = {ClientModule.FULLBRIGHT, ClientModule.SPRINT, ClientModule.FREELOOK, ClientModule.BLOCK_INFO};
                for (int i = 0; i < quick.length; i++) {
                    var toggle = new QuickToggle(x + 8, quickTop() + 18 + i * 20, CARD_WIDTH - 16, quick[i]);
                    toggle.revealDelay(i + 2);
                    result.add(toggle);
                }
                var open = new GlassButton(x + 8, waypointsTop() + 68, CARD_WIDTH - 16, 18, Component.literal("Waypoints · " + ClientBindings.label(VortexClient.config.waypointsKey)),
                    b -> WaypointsFeature.open(owner, false), false);
                open.revealDelay(6);
                result.add(open);
            }
        }
        return result;
    }
    static final int CHIP_HEIGHT = 30;
    /** Top-right player chip {x, y, width}; null when it would crowd the menu column. */
    public static int[] chip(int screenWidth, Layout column, String name) {
        int width = Math.max(112, Math.min(170, 52 + name.length() * 6));
        int margin = column.x(), x = screenWidth - margin - width;
        if (x < column.x() + column.width() + 12) return null;
        return new int[] {x, 10, width};
    }
    /** Shown cape name for the chip's second line. */
    public static String capeLabel(String id) {
        if (id == null || id.equals("none")) return "No cape";
        if (CapeCatalog.custom(id)) return "Custom cape";
        return Character.toUpperCase(id.charAt(0)) + id.substring(1) + " cape";
    }
    /** Offline profile name and the selected cape; opens the wardrobe. */
    static final class PlayerChip extends GlassButton {
        private final String name;
        private final UiMotion.Feedback motion = new UiMotion.Feedback();
        PlayerChip(int x, int y, int w, String name, Button.OnPress press) {
            super(x, y, w, CHIP_HEIGHT, Component.literal(name + " · Wardrobe"), press, false);
            this.name = name;
            setTooltip(Tooltip.create(Component.literal("Open the wardrobe")));
        }
        @Override protected void extractContents(GuiGraphicsExtractor g, int mx, int my, float delta) {
            motion.frame(active && isHoveredOrFocused(), false);
            float entrance = entrance();
            UiMotion.begin(g, getX(), getY(), getWidth(), getHeight(), pressScale(System.nanoTime()), (1 - entrance) * -8);
            ClientUi.pill(g, getX(), getY(), getWidth(), getHeight(), withAlpha(ClientUi.mix(0xC815161C, 0xE02B2E3A, motion.hover), entrance));
            int avatar = getHeight() - 8;
            ClientUi.rounded(g, getX() + 4, getY() + 4, avatar, avatar, avatar / 2, withAlpha(ClientUi.mix(ClientUi.ACCENT_DEEP, ClientUi.ACCENT_DEEP_HOVER, motion.hover), entrance));
            ClientUi.icon(g, "profile", getX() + 4 + (avatar - 12) / 2, getY() + 4 + (avatar - 12) / 2, 12, withAlpha(ClientUi.ACCENT, entrance));
            int textX = getX() + avatar + 10, textWidth = getWidth() - avatar - 18;
            ClientUi.text(g, name, textX, getY() + 5, textWidth, withAlpha(ClientUi.TEXT, entrance));
            ClientUi.text(g, capeLabel(VortexClient.config.selectedCape), textX, getY() + 16, textWidth, withAlpha(ClientUi.MUTED, entrance));
            g.pose().popMatrix();
        }
    }
    /** The singleplayer world's name or the server's list name, read once when the menu opens. */
    static String worldName(Minecraft minecraft) {
        if (minecraft == null) return "";
        var local = minecraft.getSingleplayerServer();
        if (local != null) return local.getWorldData().getLevelName();
        var remote = minecraft.getCurrentServer();
        return remote == null || remote.name == null ? "" : remote.name;
    }
    private int quickTop() { return cardsTop; }
    private int waypointsTop() { return quickTop() + 18 + 4 * 20 + 14; }
    public static String key(Component label) {
        return label.getContents() instanceof TranslatableContents translated ? translated.getKey() : "";
    }
    public static String label(Component label) {
        return switch (key(label)) {
            case "menu.singleplayer" -> "Singleplayer";
            case "menu.multiplayer" -> "Multiplayer";
            case "menu.online" -> "Realms";
            case "gui.friends.open" -> "Friends";
            case "menu.options" -> "Options";
            case "menu.quit" -> "Quit Game";
            case "menu.playdemo" -> "Play Demo";
            case "menu.resetdemo" -> "Reset Demo";
            case "options.language" -> "Language";
            case "options.accessibility", "narrator.button.accessibility" -> "Accessibility";
            case "title.credits" -> "Credits";
            case "menu.returnToGame" -> "Back to Game";
            case "gui.advancements" -> "Advancements";
            case "gui.stats" -> "Statistics";
            case "menu.sendFeedback" -> "Give Feedback";
            case "menu.reportBugs" -> "Report Bugs";
            case "menu.playerReporting" -> "Player Reporting";
            case "menu.shareToLan" -> "Open to LAN";
            case "options.worldOptions.button" -> "World Options";
            case "menu.returnToMenu" -> "Save & Quit";
            case "menu.savingLevel" -> "Saving world...";
            case "menu.disconnect" -> "Disconnect";
            case "gui.socialInteractions" -> "Social Interactions";
            default -> label.getString().isBlank() ? "More options" : label.getString();
        };
    }
    private static boolean leaves(String key) {
        return key.equals("menu.returnToMenu") || key.equals("menu.disconnect");
    }
    public static Button wrap(Button source, int x, int y, int w, int h, boolean primary) {
        return new MenuButton(source, x, y, w, h, primary, MenuButton.PILL);
    }
    /** Soft pill used by both menus: lavender for the main action, translucent for the rest. */
    static class GlassButton extends ClientUi.ActionButton {
        private final boolean primary;
        private long born = System.nanoTime();
        private boolean accent, danger;
        private float hover;
        private long previousFrame = System.nanoTime();
        GlassButton(int x, int y, int w, int h, Component label, Button.OnPress press, boolean primary) {
            super(x, y, w, h, label, press, primary); this.primary = primary;
        }
        /** Staggered entrance: each slot rises a little after the one above it. */
        void revealDelay(int slot) { born = System.nanoTime() + Math.min(slot, 12) * 35_000_000L; }
        void accent() { accent = true; }
        void danger(boolean value) { danger = value; }
        float entrance() { return UiMotion.progress(born); }
        @Override protected void extractContents(GuiGraphicsExtractor g, int mx, int my, float delta) {
            long now = System.nanoTime();
            hover = ClientUi.approach(hover, active && isHoveredOrFocused() ? 1 : 0, (now - previousFrame) / 1e9);
            previousFrame = now;
            float entrance = entrance();
            UiMotion.begin(g, getX(), getY(), getWidth(), getHeight(), pressScale(now), (1 - entrance) * 8 - (VortexClient.config.uiAnimations ? hover * .5f : 0));
            int background = !active ? 0xC01E2028
                : primary ? ClientUi.mix(ClientUi.ACCENT, ClientUi.ACCENT_LIGHT, hover)
                : danger ? ClientUi.mix(0xC0341C24, 0xE04A2430, hover)
                : accent ? ClientUi.mix(ClientUi.ACCENT_TINT & 0xFFFFFF | 0xC0000000, ClientUi.ACCENT_TINT_STRONG & 0xFFFFFF | 0xE0000000, hover)
                : ClientUi.mix(0xC815161C, 0xE02B2E3A, hover);
            ClientUi.pill(g, getX(), getY(), getWidth(), getHeight(), withAlpha(background, entrance));
            var font = UiFonts.body();
            String label = font.plainSubstrByWidth(getMessage().getString(), getWidth() - 14);
            int color = !active ? 0xFF888A93 : primary ? ClientUi.ACCENT_TEXT : danger ? ClientUi.DANGER : ClientUi.TEXT;
            g.text(font, label, getX() + (getWidth() - font.width(label)) / 2, getY() + (getHeight() - 9) / 2, withAlpha(color, entrance), false);
            g.pose().popMatrix();
        }
    }
    /** Scales a colour's alpha by an entrance so surfaces fade up instead of popping in. */
    static int withAlpha(int color, float amount) {
        if (!VortexClient.config.uiAnimations) return color;
        return ((int)((color >>> 24) * Math.clamp(amount, 0, 1)) << 24) | (color & 0xFFFFFF);
    }
    /** Small, individually labeled shortcuts; narration and tooltips keep icons unambiguous. */
    static final class DockButton extends GlassButton {
        private final String icon;
        private final UiMotion.Feedback motion=new UiMotion.Feedback();
        DockButton(int x, int y, String icon, String label, Button.OnPress press) {
            super(x, y, 24, 24, Component.literal(label), press, false);
            this.icon = icon;
            setTooltip(Tooltip.create(Component.literal(label)));
        }
        @Override protected void extractContents(GuiGraphicsExtractor g, int mx, int my, float delta) {
            motion.frame(isHoveredOrFocused(),false);
            drawDock(g, getX(), getY(), icon, active, motion.hover, pressScale(System.nanoTime()));
        }
    }
    /** Visible labels, distinct artwork and the same keyboard/narration support as regular buttons. */
    static final class ShortcutButton extends GlassButton {
        private final String icon;
        private final UiMotion.Feedback motion = new UiMotion.Feedback();
        private boolean raised;
        ShortcutButton(int x, int y, int w, int h, String icon, String label, Button.OnPress press) {
            super(x, y, w, h, Component.literal(label), press, false);
            this.icon = icon;
            setTooltip(Tooltip.create(Component.literal(label)));
        }
        /** Inside a window: the same raised fill and centred content as the plain buttons next to it. */
        ShortcutButton raised() { raised = true; return this; }
        @Override protected void extractContents(GuiGraphicsExtractor g, int mx, int my, float delta) {
            motion.frame(active && isHoveredOrFocused(), false);
            if (raised) drawRaised(g, getX(), getY(), getWidth(), getHeight(), icon, getMessage().getString(), active, motion.hover, pressScale(System.nanoTime()));
            else drawShortcut(g, getX(), getY(), getWidth(), getHeight(), icon, getMessage().getString(), active, motion.hover, pressScale(System.nanoTime()), entrance(), false);
        }
    }
    /** One module switch on the pause screen; the same toggle and save path as the module menu. */
    static final class QuickToggle extends GlassButton {
        private final ClientModule module;
        private final UiMotion.Feedback motion = new UiMotion.Feedback();
        private float state;
        QuickToggle(int x, int y, int w, ClientModule module) {
            super(x, y, w, 18, Component.literal(module.title), b -> { }, false);
            this.module = module;
            state = module.enabled() ? 1 : 0;
            setTooltip(Tooltip.create(Component.literal(module.description)));
        }
        @Override public void onPress(InputWithModifiers input) {
            markPress();
            module.toggle();
            if (!VortexClient.config.save()) {
                module.toggle();
                ClientNotifications.error("quick-toggle", "Settings not saved", "Could not save " + module.title + ". The previous setting was kept.");
            }
        }
        @Override protected void extractContents(GuiGraphicsExtractor g, int mx, int my, float delta) {
            motion.frame(active && isHoveredOrFocused(), false);
            long now = System.nanoTime();
            state = ClientUi.approach(state, module.enabled() ? 1 : 0, 1 / 60.0);
            float entrance = entrance();
            UiMotion.begin(g, getX(), getY(), getWidth(), getHeight(), pressScale(now), (1 - entrance) * 6);
            if (motion.hover > .01f) ClientUi.pill(g, getX() - 4, getY(), getWidth() + 8, getHeight(), withAlpha(((int)(motion.hover * 0x22) << 24) | 0xFFFFFF, entrance));
            ClientUi.text(g, module.title, getX(), getY() + (getHeight() - 9) / 2, getWidth() - 34, withAlpha(ClientUi.TEXT, entrance));
            ClientUi.toggle(g, getX() + getWidth() - 28, getY() + (getHeight() - 14) / 2, state);
            g.pose().popMatrix();
        }
    }
    private static void drawShortcut(GuiGraphicsExtractor g, int x, int y, int w, int h, String icon, String label, boolean active, float hover, float scale, float entrance, boolean danger) {
        UiMotion.begin(g, x, y, w, h, scale, (1 - entrance) * 8 - (VortexClient.config.uiAnimations ? hover * .5f : 0));
        int background = danger ? ClientUi.mix(0xC0341C24, 0xE04A2430, active ? hover : 0) : ClientUi.mix(0xC815161C, 0xE02B2E3A, active ? hover : 0);
        ClientUi.pill(g, x, y, w, h, withAlpha(background, entrance));
        int color = !active ? ClientUi.MUTED : danger ? ClientUi.DANGER : ClientUi.TEXT;
        ClientUi.icon(g, icon, x + 8, y + (h - 12) / 2, 12, withAlpha(danger ? color : ClientUi.mix(color, ClientUi.ACCENT, hover), entrance));
        ClientUi.text(g, label, x + 25, y + (h - 9) / 2, w - 31, withAlpha(color, entrance));
        g.pose().popMatrix();
    }
    private static void drawRaised(GuiGraphicsExtractor g, int x, int y, int w, int h, String icon, String label, boolean active, float hover, float scale) {
        UiMotion.begin(g, x, y, w, h, scale, VortexClient.config.uiAnimations ? -hover * .5f : 0);
        ClientUi.pill(g, x, y, w, h, !active ? 0xFF1E2028 : ClientUi.mix(ClientUi.RAISED, 0xFF383C4B, hover));
        var font = UiFonts.body();
        String text = font.plainSubstrByWidth(label, w - 34);
        int left = x + (w - (12 + 6 + font.width(text))) / 2;
        int color = active ? ClientUi.TEXT : 0xFF6B6E7B;
        ClientUi.icon(g, icon, left, y + (h - 12) / 2, 12, ClientUi.mix(color, ClientUi.ACCENT, hover));
        g.text(font, text, left + 18, y + (h - 9) / 2, color, false);
        g.pose().popMatrix();
    }
    private static void drawDock(GuiGraphicsExtractor g, int x, int y, String icon, boolean active, float hover, float scale) {
        UiMotion.begin(g, x, y, 24, 24, scale, VortexClient.config.uiAnimations ? -hover : 0);
        ClientUi.rounded(g, x, y, 24, 24, 12, ClientUi.mix(0xC815161C, 0xE02B2E3A, hover));
        ClientUi.icon(g, icon, x + 5, y + 5, 14, active ? ClientUi.mix(ClientUi.TEXT, ClientUi.ACCENT, hover) : ClientUi.MUTED);
        g.pose().popMatrix();
    }
    public static String shortcutIcon(String key) {
        return switch (key) {
            case "menu.options" -> "settings";
            case "options.accessibility", "narrator.button.accessibility" -> "accessibility";
            case "options.worldOptions.button" -> "world";
            case "options.language" -> "language";
            case "menu.online" -> "realms";
            case "menu.shareToLan" -> "lan";
            case "menu.multiplayer" -> "globe";
            case "menu.quit", "menu.returnToMenu", "menu.disconnect" -> "exit";
            case "gui.stats" -> "statistics";
            case "gui.advancements" -> "trophy";
            case "menu.sendFeedback" -> "feedback";
            case "menu.reportBugs" -> "bug";
            case "menu.playerReporting" -> "report";
            case "gui.socialInteractions", "gui.friends.open" -> "social";
            case "title.credits" -> "credits";
            case "menu.resetdemo" -> "reset";
            default -> "help";
        };
    }
    private static final class MenuButton extends GlassButton {
        /** PILL: large column action; SHORTCUT: labeled icon pill; ICON: round icon; LINK: footer text. */
        static final int PILL = 0, SHORTCUT = 1, ICON = 2, LINK = 3;
        private final Button source;
        private final int style;
        private final UiMotion.Feedback motion=new UiMotion.Feedback();
        MenuButton(Button source, int x, int y, int w, int h, boolean primary, int style) {
            super(x, y, w, h, Component.literal(style == LINK ? source.getMessage().getString() : label(source.getMessage())), b -> { }, primary);
            this.source = source; this.style = style; sync();
            setTooltip(Tooltip.create(Component.literal(label(source.getMessage()))));
        }
        private void sync() {
            active = source.active; visible = source.visible;
            source.setPosition(getX(), getY());
            source.setWidth(getWidth()); source.setHeight(getHeight());
            String text = style == LINK ? source.getMessage().getString() : label(source.getMessage());
            if (!getMessage().getString().equals(text)) setMessage(Component.literal(text));
            danger(leaves(key(source.getMessage())));
        }
        @Override public void onPress(InputWithModifiers input) {
            sync();
            if (active && visible) { markPress(); source.onPress(input); }
        }
        @Override protected void extractContents(GuiGraphicsExtractor g, int mx, int my, float delta) {
            sync();
            String key = key(source.getMessage());
            if (style == SHORTCUT) {
                motion.frame(active && isHoveredOrFocused(),false);
                drawShortcut(g, getX(), getY(), getWidth(), getHeight(), shortcutIcon(key), getMessage().getString(), active, motion.hover, pressScale(System.nanoTime()), entrance(), leaves(key));
            } else if (style == ICON) {
                motion.frame(active && isHoveredOrFocused(),false);
                UiMotion.begin(g, getX(), getY(), getWidth(), getHeight(), 1, (1 - entrance()) * 8);
                drawDock(g, getX(), getY(), shortcutIcon(key), active, motion.hover, pressScale(System.nanoTime()));
                g.pose().popMatrix();
            } else if (style == LINK) {
                motion.frame(active && isHoveredOrFocused(),false);
                var font = UiFonts.body();
                String text = font.plainSubstrByWidth(getMessage().getString(), getWidth());
                int color = ClientUi.mix(0xFFD5D7DE, ClientUi.ACCENT, motion.hover);
                g.text(font, text, getX() + 2, getY() + 2, color, false);
                if (motion.hover > .5f) g.fill(getX() + 2, getY() + 11, getX() + 2 + font.width(text), getY() + 12, color);
            }
            else super.extractContents(g, mx, my, delta);
        }
    }
    public static void drawBrand(GuiGraphicsExtractor g, int center, int y, boolean compact, String caption) {
        int size = compact ? 22 : 38;
        ClientUi.icon(g, "brand", center - size / 2, y, size, ClientUi.TEXT);
        var font = UiFonts.heading();
        g.text(font, caption, center - font.width(caption) / 2, y + size + 5, ClientUi.TEXT, false);
    }
    /** Brand block aligned to the menu column: emblem, name and a small caption. */
    private static void drawColumnBrand(GuiGraphicsExtractor g, int x, int y, int width, boolean compact, String title, String caption, float fade) {
        int size = compact ? 22 : 32;
        int offset = Math.round((1 - fade) * 6);
        ClientUi.rounded(g, x, y + offset, size, size, Math.min(12, size / 3), withAlpha(ClientUi.ACCENT_DEEP, fade));
        ClientUi.icon(g, "brand", x + 3, y + 3 + offset, size - 6, withAlpha(ClientUi.TEXT, fade));
        int textX = x + size + 8;
        ClientUi.heading(g, title, textX, y + (compact ? 2 : 5) + offset, width - size - 8);
        if (!compact) ClientUi.text(g, caption, textX, y + 20 + offset, width - size - 8, withAlpha(ClientUi.MUTED, fade));
    }
    public void draw(GuiGraphicsExtractor g, int width, int height) {
        for (var button : wrapped) button.sync();
        float fade = UiMotion.progress(opened);
        g.fill(0, 0, width, height, ((int)((pause ? 0x70 : 0x30) * fade) << 24) | 0x0C0D12);
        int x = layout.x(), y = layout.y(), w = layout.width();
        boolean compact = layout.header() < 60;
        String gameVersion = net.fabricmc.loader.api.FabricLoader.getInstance().getModContainer("minecraft").map(m -> m.getMetadata().getVersion().getFriendlyString()).orElse("");
        if (pause) {
            // The pause panel slides in from the left while the world dims behind it.
            int slide = Math.round((1 - fade) * -14);
            ClientUi.rounded(g, x - 10 + slide, y - 10, w + 20, layout.contentBottom() - y + 20, 12, withAlpha(ClientUi.GLASS, fade));
            String title = worldName.isBlank() ? "Game paused" : worldName;
            String caption = worldName.isBlank() ? "Vortex Client · Minecraft " + gameVersion : "Game paused · Vortex Client";
            drawColumnBrand(g, x + slide, y + (compact ? 4 : 8), w, compact, title, caption, fade);
            if (cardsX >= 0) drawCards(g, fade);
        } else {
            drawColumnBrand(g, x, y + (compact ? 4 : 8), w, compact, "Vortex", "Client for Minecraft " + gameVersion, fade);
            if (dock != null) {
                // The glass tray rises with its buttons, a little after the column.
                int rise = Math.round((1 - fade) * 10);
                ClientUi.pill(g, dock.x(), dock.y() + rise, dock.width(), dock.height(), withAlpha(ClientUi.GLASS, fade));
            }
        }
        // The title screen's bottom-left is the copyright link; the version is already in the brand caption.
        if (pause) g.text(UiFonts.body(), "Minecraft " + gameVersion + " · Fabric · Vortex Client", 10, height - 14, 0xFFD5D7DE, false);
        String hint = ClientBindings.label(VortexClient.config.menuKey) + " · Vortex Client";
        g.text(UiFonts.body(), hint, width - UiFonts.body().width(hint) - 10, height - 14, 0xFFD5D7DE, false);
    }
    private void drawCards(GuiGraphicsExtractor g, float fade) {
        int x = cardsX, offset = Math.round((1 - fade) * 14);
        int quick = quickTop();
        ClientUi.rounded(g, x + offset, quick - 6, CARD_WIDTH, 18 + 4 * 20 + 6, 12, withAlpha(ClientUi.GLASS, fade));
        ClientUi.eyebrow(g, "Quick toggles", x + 10 + offset, quick + 2, CARD_WIDTH - 20);
        int top = waypointsTop();
        ClientUi.rounded(g, x + offset, top - 6, CARD_WIDTH, 98, 12, withAlpha(ClientUi.GLASS, fade));
        ClientUi.eyebrow(g, "Waypoints", x + 10 + offset, top + 2, CARD_WIDTH - 20);
        var minecraft = Minecraft.getInstance();
        var player = minecraft == null ? null : minecraft.player;
        var nearest = player == null ? List.<WaypointsStore.Point>of() : WaypointsFeature.points().stream()
            .filter(p -> !p.hidden())
            .sorted(Comparator.comparingDouble(p -> distance(p, player.getX(), player.getZ())))
            .limit(2).toList();
        if (nearest.isEmpty()) ClientUi.text(g, "No waypoints in this world yet", x + 10 + offset, top + 20, CARD_WIDTH - 20, ClientUi.MUTED);
        for (int i = 0; i < nearest.size(); i++) {
            var point = nearest.get(i);
            int rowY = top + 16 + i * 24;
            ClientUi.rounded(g, x + 10 + offset, rowY + 5, 8, 8, 4, 0xFF000000 | point.color());
            ClientUi.text(g, point.name(), x + 24 + offset, rowY, CARD_WIDTH - 34, ClientUi.TEXT);
            String detail = point.x() + ", " + point.y() + ", " + point.z() + " · " + Math.round(distance(point, player.getX(), player.getZ())) + " m";
            ClientUi.text(g, detail, x + 24 + offset, rowY + 10, CARD_WIDTH - 34, ClientUi.MUTED);
        }
    }
    private static double distance(WaypointsStore.Point point, double x, double z) {
        return Math.hypot(point.x() + .5 - x, point.z() + .5 - z);
    }
}
