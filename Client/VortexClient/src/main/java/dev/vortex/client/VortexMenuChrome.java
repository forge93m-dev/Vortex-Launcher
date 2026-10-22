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
    public record Layout(int x, int y, int width, int height, int columns, int rows, int buttonWidth, int buttonHeight,
                         int gap, int header, int primaryCount, int utilityHeight, int utilityY) {
        public int utilityWidth() { return (width - (columns - 1) * gap) / columns; }
        public int widthFor(int index) { return index < primaryCount ? buttonWidth : utilityWidth(); }
        public int heightFor(int index) { return index < primaryCount ? buttonHeight : utilityHeight; }
        public int buttonX(int index) { return index < primaryCount ? x + (width - buttonWidth) / 2 : x + (index - primaryCount) % columns * (utilityWidth() + gap); }
        public int buttonY(int index) { return index < primaryCount ? y + header + index * (buttonHeight + gap) : y + utilityY + (index - primaryCount) / columns * (utilityHeight + gap); }
    }
    private final Screen owner;
    private final boolean pause;
    private final List<Button> sources;
    private final long opened = System.nanoTime();
    private final List<MenuButton> wrapped = new ArrayList<>();
    private Layout layout;

    public VortexMenuChrome(Screen owner, boolean pause, List<Button> sources) {
        this.owner = owner; this.pause = pause;
        this.sources = sources.stream().sorted(Comparator.comparingInt(b -> priority(key(b.getMessage())))).toList();
    }
    private static int priority(String key) {
        return switch (key) {
            case "menu.singleplayer", "menu.playdemo", "menu.returnToGame" -> 0;
            case "menu.multiplayer" -> 1;
            case "menu.options" -> 2;
            case "menu.online" -> 3;
            case "options.language" -> 4;
            case "options.accessibility", "narrator.button.accessibility" -> 5;
            case "title.credits" -> 8;
            case "menu.quit", "menu.returnToMenu", "menu.disconnect" -> 9;
            default -> 6;
        };
    }
    public static Layout layout(int screenWidth, int screenHeight, int buttonCount) {
        return layout(screenWidth, screenHeight, buttonCount, false);
    }
    public static Layout layout(int screenWidth, int screenHeight, int buttonCount, boolean pause) {
        int count = Math.max(1, buttonCount), w = Math.min(520, Math.max(40, screenWidth - 24));
        int primaryCount = Math.min(count, pause ? 1 : 2);
        int header = screenHeight < 300 ? 38 : 64, gap = screenHeight < 300 ? 2 : 6;
        int buttonHeight = screenHeight < 300 ? 20 : 28, utilityHeight = screenHeight < 300 ? 20 : 28;
        int utilityCount = count - primaryCount;
        int columns = Math.max(1, Math.min(Math.max(1, utilityCount), (w + gap) / 112));
        // Keep readable labels at ordinary scales; a denser grid fits unusual mod-added actions.
        int available = screenHeight - 38 - header - primaryCount * (buttonHeight + gap) - 28;
        while (columns < Math.max(1, utilityCount) &&
               ((utilityCount + columns - 1) / columns) * (utilityHeight + gap) > available &&
               (w - columns * gap) / (columns + 1) >= 64) columns++;
        int rows = (utilityCount + columns - 1) / columns;
        int centerHeight = header + primaryCount * (buttonHeight + gap) + 28;
        int dockTop = screenHeight - 22 - rows * (utilityHeight + gap);
        int y = Math.max(8, Math.min((screenHeight - centerHeight) / 2 - 12, dockTop - centerHeight - 8));
        int utilityY = dockTop - y;
        return new Layout((screenWidth - w) / 2, y, w, screenHeight - y - 4, columns, rows,
            Math.min(260, w), buttonHeight, gap, header, primaryCount, utilityHeight, utilityY);
    }
    public List<Button> widgets(int width, int height) {
        layout = layout(width, height, sources.size() + 1, pause);
        wrapped.clear();
        var result = new ArrayList<Button>();
        for (int i = 0; i < sources.size(); i++) {
            var source = sources.get(i);
            String key = key(source.getMessage());
            boolean primary = key.equals("menu.singleplayer") || key.equals("menu.playdemo") || key.equals("menu.returnToGame");
            int slot = i < layout.primaryCount() ? i : i + 1;
            var button = new MenuButton(source, layout.buttonX(slot), layout.buttonY(slot), layout.widthFor(slot), layout.heightFor(slot), primary, slot >= layout.primaryCount());
            wrapped.add(button); result.add(button);
        }
        int last = Math.min(layout.primaryCount(), sources.size());
        result.add(new ShortcutButton(layout.buttonX(last), layout.buttonY(last), layout.widthFor(last), layout.heightFor(last), "brand", "Vortex Client",
            b -> Minecraft.getInstance().gui.setScreen(new VortexHomeScreen(owner))));
        int pairWidth = (layout.buttonWidth() - 4) / 2;
        int centerX = (width - layout.buttonWidth()) / 2;
        int centerY = layout.y() + layout.header() + layout.primaryCount() * (layout.buttonHeight() + layout.gap());
        result.add(new GlassButton(centerX, centerY, pairWidth, 22, Component.literal("Modules"),
            b -> Minecraft.getInstance().gui.setScreen(new VortexScreen(owner)), false));
        result.add(new GlassButton(centerX + pairWidth + 4, centerY, pairWidth, 22, Component.literal("Wardrobe"),
            b -> Minecraft.getInstance().gui.setScreen(new WardrobeScreen(owner)), false));
        return result;
    }
    public static String key(Component label) {
        return label.getContents() instanceof TranslatableContents translated ? translated.getKey() : "";
    }
    public static String label(Component label) {
        return switch (key(label)) {
            case "menu.singleplayer" -> "Singleplayer";
            case "menu.multiplayer" -> "Multiplayer";
            case "menu.online" -> "Minecraft Realms";
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
    public static Button wrap(Button source, int x, int y, int w, int h, boolean primary) {
        return new MenuButton(source, x, y, w, h, primary, false);
    }
    static class GlassButton extends ClientUi.ActionButton {
        private final boolean primary;
        private final long born = System.nanoTime();
        private float hover;
        private long previousFrame = System.nanoTime();
        GlassButton(int x, int y, int w, int h, Component label, Button.OnPress press, boolean primary) {
            super(x, y, w, h, label, press, primary); this.primary = primary;
        }
        @Override protected void extractContents(GuiGraphicsExtractor g, int mx, int my, float delta) {
            long now = System.nanoTime();
            hover = ClientUi.approach(hover, active && isHoveredOrFocused() ? 1 : 0, (now - previousFrame) / 1e9);
            previousFrame = now;
            float entrance = UiMotion.progress(born);
            UiMotion.begin(g, getX(), getY(), getWidth(), getHeight(), pressScale(now), (1 - entrance) * 7 - (VortexClient.config.uiAnimations ? hover : 0));
            int background = primary ? ClientUi.mix(0xFFD2E8C9, 0xFFE3F3DF, hover) : ClientUi.mix(0xFF15191F, 0xFF252D34, hover);
            ClientUi.panel(g, getX(), getY(), getWidth(), getHeight(), 6,
                primary ? 0xFFD2E8C9 : ClientUi.mix(ClientUi.BORDER, 0xFF69758B, hover * .65f), active ? background : 0xFF13151A);
            var font = UiFonts.body();
            String label = font.plainSubstrByWidth(getMessage().getString(), getWidth() - 12);
            g.text(font, label, getX() + (getWidth() - font.width(label)) / 2, getY() + (getHeight() - 9) / 2,
                !active ? 0xFF888A93 : primary ? 0xFF162319 : ClientUi.TEXT, false);
            g.pose().popMatrix();
        }
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
        ShortcutButton(int x, int y, int w, int h, String icon, String label, Button.OnPress press) {
            super(x, y, w, h, Component.literal(label), press, false);
            this.icon = icon;
            setTooltip(Tooltip.create(Component.literal(label)));
        }
        @Override protected void extractContents(GuiGraphicsExtractor g, int mx, int my, float delta) {
            motion.frame(active && isHoveredOrFocused(), false);
            drawShortcut(g, getX(), getY(), getWidth(), getHeight(), icon, getMessage().getString(), active, motion.hover, pressScale(System.nanoTime()));
        }
    }
    private static void drawShortcut(GuiGraphicsExtractor g, int x, int y, int w, int h, String icon, String label, boolean active, float hover, float scale) {
        UiMotion.begin(g, x, y, w, h, scale, 0);
        ClientUi.panel(g, x, y, w, h, 6, ClientUi.mix(ClientUi.BORDER, 0xFF657568, hover * .65f),
            ClientUi.mix(0xEE151A1D, 0xFF29332D, active ? hover : 0));
        int color = active ? ClientUi.TEXT : ClientUi.MUTED;
        ClientUi.icon(g, icon, x + 8, y + (h - 14) / 2, 14, color);
        ClientUi.text(g, label, x + 28, y + (h - 9) / 2, w - 34, color);
        g.pose().popMatrix();
    }
    private static void drawDock(GuiGraphicsExtractor g, int x, int y, String icon, boolean active, float hover, float scale) {
        UiMotion.begin(g, x, y, 24, 24, scale, VortexClient.config.uiAnimations ? -hover : 0);
        ClientUi.rounded(g, x, y, 24, 24, 6, ClientUi.mix(0xFF181D25,0xFF353E4A,hover));
        ClientUi.icon(g, icon, x + 5, y + 5, 14, active ? ClientUi.TEXT : ClientUi.MUTED);
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
            case "gui.socialInteractions" -> "social";
            case "title.credits" -> "credits";
            case "menu.resetdemo" -> "reset";
            default -> "help";
        };
    }
    private static final class MenuButton extends GlassButton {
        private final Button source;
        private final boolean dock;
        private final UiMotion.Feedback motion=new UiMotion.Feedback();
        MenuButton(Button source, int x, int y, int w, int h, boolean primary, boolean dock) {
            super(x, y, w, h, Component.literal(label(source.getMessage())), b -> { }, primary);
            this.source = source; this.dock = dock; sync();
            setTooltip(Tooltip.create(Component.literal(label(source.getMessage()))));
        }
        private void sync() {
            active = source.active; visible = source.visible;
            source.setPosition(getX(), getY());
            source.setWidth(getWidth()); source.setHeight(getHeight());
            String text = label(source.getMessage());
            if (!getMessage().getString().equals(text)) setMessage(Component.literal(text));
        }
        @Override public void onPress(InputWithModifiers input) {
            sync();
            if (active && visible) { markPress(); source.onPress(input); }
        }
        @Override protected void extractContents(GuiGraphicsExtractor g, int mx, int my, float delta) {
            sync();
            if (dock) {motion.frame(active && isHoveredOrFocused(),false);drawShortcut(g, getX(), getY(), getWidth(), getHeight(), shortcutIcon(key(source.getMessage())), getMessage().getString(), active, motion.hover, pressScale(System.nanoTime()));}
            else super.extractContents(g, mx, my, delta);
        }
    }
    public static void drawBrand(GuiGraphicsExtractor g, int center, int y, boolean compact, String caption) {
        int size = compact ? 22 : 38;
        ClientUi.icon(g, "brand", center - size / 2, y, size, ClientUi.TEXT);
        var font = UiFonts.heading();
        g.text(font, caption, center - font.width(caption) / 2, y + size + 5, ClientUi.TEXT, false);
    }
    public void draw(GuiGraphicsExtractor g, int width, int height) {
        for (var button : wrapped) button.sync();
        int shade = pause ? 0x5506080C : 0x3506080C;
        g.fill(0, 0, width, height, shade);
        int x = layout.x(), y = layout.y(), w = layout.width(), h = layout.height();
        float fade = UiMotion.progress(opened);
        int centerWidth = layout.buttonWidth() + 20;
        int centerHeight = layout.header() + layout.primaryCount() * (layout.buttonHeight() + layout.gap()) + 30;
        ClientUi.panel(g, (width - centerWidth) / 2, y - 6, centerWidth, centerHeight + 8, 10, 0x6644534B, 0xC011191C);
        drawBrand(g, width / 2, y + (int)((1 - fade) * 4), layout.header() < 60, pause ? "Pause" : "VORTEX CLIENT");
        if (layout.utilityY() > 18) {
            int top = y + layout.utilityY();
            ClientUi.rounded(g, x, top - 8, w, 1, 0, 0x55697770);
        }
        String gameVersion=net.fabricmc.loader.api.FabricLoader.getInstance().getModContainer("minecraft").map(m->m.getMetadata().getVersion().getFriendlyString()).orElse("");
        g.text(UiFonts.body(), "Minecraft " + gameVersion + " / Vortex", 10, height - 14, 0xFFD5D7DE, false);
        String hint = ClientBindings.label(VortexClient.config.menuKey) + " / Vortex Client";
        g.text(UiFonts.body(), hint, width - UiFonts.body().width(hint) - 10, height - 14, 0xFFD5D7DE, false);
    }
}
