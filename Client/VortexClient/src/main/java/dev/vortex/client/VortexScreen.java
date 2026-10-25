package dev.vortex.client;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Locale;
import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.MouseButtonInfo;
import net.minecraft.network.chat.Component;

public final class VortexScreen extends Screen {
    private final Screen parent;
    private long pageOpened = System.nanoTime();
    private final long opened = System.nanoTime();
    private String saveMessage = "Per-instance settings";
    private String query = "", filter = "All Modules";
    private boolean settingsPage;
    private boolean dirtySettings;
    private ClientModule selectedModule;
    private int matchingCount;
    private double scroll, scrollTarget, scrollbarGrab;
    private long scrollFrame = System.nanoTime();
    private boolean scrollbarDragging;
    private MenuLayout layout;
    private final List<AbstractWidget> cards = new ArrayList<>();

    public VortexScreen(Screen parent) { this(parent, null); }
    public static VortexScreen settings(Screen parent) {
        var screen = new VortexScreen(parent); screen.settingsPage = true; return screen;
    }
    public VortexScreen(Screen parent, ClientModule module) {
        super(net.minecraft.client.Minecraft.getInstance(), UiFonts.body(), Component.literal("Vortex Client"));
        this.parent = parent;
        this.selectedModule = module;
        this.settingsPage = module != null;
    }

    /** GUI-scaled dimensions, also exercised without opening a game window. */
    public record MenuLayout(int x, int y, int width, int height, int sidebar,
                             int columns, int rows, int cardWidth, int cardHeight) {
        public int contentX() { return x + sidebar + 14; }
        public int contentWidth() { return width - sidebar - 28; }
        public int capacity() { return columns * rows; }
        public int sideX() { return x + 12; }
        /** Rows keep a 6 px margin inside the sidebar strip on every side. */
        public int sideWidth() { return sidebar - 12; }
        /** Sidebar rows: four categories, then the Vortex section; Settings sits at the bottom. */
        public int sideY(int index) { return y + 62 + index * (SIDE_ROW + 2) + (index >= CATEGORIES.length ? 26 : 0); }
        public int sideSettingsY() { return y + height - 32; }
    }
    static final int SIDE_ROW = 20, SIDEBAR = 148;
    static final String[] CATEGORIES = {"All Modules", "HUD", "Gameplay", "Enabled"};
    static final String[][] CATEGORY_ITEMS = {{"modules", "All"}, {"hud", "HUD"}, {"play", "Gameplay"}, {"sprint", "Enabled"}};
    static final String[][] VORTEX_ITEMS = {{"capes", "Wardrobe"}, {"social", "Party"}, {"chat", "Discord"}, {"keybinds", "Key bindings"}};
    /** Modules shown under a category with an empty search. */
    public static int categoryCount(String category) {
        return (int)Arrays.stream(ClientModule.values()).filter(module -> module.matches("", category)).count();
    }
    public static MenuLayout menuLayout(int screenWidth, int screenHeight) {
        int w = Math.min(680, Math.max(1, screenWidth - 16));
        int h = Math.min(400, Math.max(1, screenHeight - 16));
        // The sidebar needs room for both sections above the Settings row; small windows keep the pill row.
        int sidebar = w >= 520 && h >= 300 ? SIDEBAR : 0;
        int content = Math.max(1, w - sidebar - 28);
        int columns = content >= 380 ? 2 : 1;
        int cardHeight = Math.min(72, Math.max(64, h - 158));
        int rows = Math.max(1, (h - 150 + 8) / (cardHeight + 8));
        return new MenuLayout((screenWidth - w) / 2, (screenHeight - h) / 2, w, h, sidebar,
            columns, rows, Math.max(1, (content - (columns - 1) * 8) / columns), cardHeight);
    }

    @Override protected void init() {
        cards.clear();
        scrollbarDragging = false;
        layout = menuLayout(width, height);
        String[] filters = CATEGORIES;
        String[] labels = {"All", "HUD", "Gameplay", "Enabled"};
        boolean side = layout.sidebar() > 0;
        if (side) initSidebar();
        for (int i = 0; !side && !settingsPage && i < filters.length; i++) {
            String category = filters[i];
            int tabWidth = (layout.contentWidth() - 12) / 4;
            addRenderableWidget(new FilterPill(layout.contentX() + i * (tabWidth + 4), layout.y() + 43,
                tabWidth, 22, Component.literal(labels[i]), () -> filter.equals(category), button -> {
                    selectFilter(category);
                }));
        }
        if (!side) {
            addRenderableWidget(new ClientUi.ActionButton(layout.contentX(), layout.y() + layout.height() - 28,
                80, 20, Component.literal(settingsPage ? "Modules" : "Edit HUD"), button -> {
                    if (settingsPage) changePage(false, null);
                    else
                    minecraft.gui.setScreen(new HudEditorScreen(this));
                }, true));
            addRenderableWidget(new ClientUi.ActionButton(layout.contentX() + 86, layout.y() + layout.height() - 28,
                80, 20, Component.literal("Settings"), button -> {
                    changePage(true, null);
                }));
        } else if (!settingsPage) {
            addRenderableWidget(new ClientUi.ActionButton(layout.x() + layout.width() - 113, headerButtonY(),
                76, 22, Component.literal("HUD Editor"), button -> minecraft.gui.setScreen(new HudEditorScreen(this)), true));
        }
        addRenderableWidget(new ClientUi.ActionButton(layout.x() + layout.width() - 31, headerButtonY(),
            22, 22, Component.literal("×"), button -> onClose()));
        if (settingsPage) { initSettings(); for(var child:children())if(child instanceof AbstractWidget widget && widget.getY()>=layout.y()+60)SettingsHelp.attach(widget,selectedModule); }
        else {
            var search = new EditBox(font, layout.contentX() + 27, layout.y() + searchY() + 8,
                layout.contentWidth() - 36, 16, Component.literal("Search modules"));
            search.setBordered(false);
            search.setTextShadow(false);
            search.setTextColor(ClientUi.TEXT);
            search.setHint(Component.literal("Search modules..."));
            search.setMaxLength(64);
            search.setValue(query);
            search.setResponder(value -> { query = value; resetScroll(); rebuildCards(); });
            addRenderableWidget(search);
            rebuildCards();
        }
    }

    /**
     * Header buttons (×, ?, HUD Editor) share the title row's centre: the settings page header icon is
     * 22 px at y+7, the modules page brand is 26 px at y+9.
     */
    int headerButtonY() { return layout.y() + (settingsPage ? 7 : 11); }
    /** Search sits under the title beside the sidebar, or under the category pills without it. */
    private int searchY() { return layout.sidebar() > 0 ? 43 : 72; }
    private void initSidebar() {
        for (int i = 0; i < CATEGORIES.length; i++) {
            String category = CATEGORIES[i];
            int count = categoryCount(category);
            addRenderableWidget(new SideItem(layout.sideX(), layout.sideY(i), layout.sideWidth(), CATEGORY_ITEMS[i][0], CATEGORY_ITEMS[i][1],
                () -> category.equals("Enabled") ? Integer.toString(categoryCount("Enabled")) : Integer.toString(count),
                () -> !settingsPage && filter.equals(category), b -> selectFilter(category)));
        }
        Runnable[] actions = {
            () -> minecraft.gui.setScreen(new WardrobeScreen(this)),
            () -> minecraft.gui.setScreen(new PartyScreen(this)),
            () -> minecraft.gui.setScreen(new DiscordPresenceScreen(this)),
            () -> minecraft.gui.setScreen(new KeybindScreen(this))
        };
        for (int i = 0; i < VORTEX_ITEMS.length; i++) {
            final int index = i;
            addRenderableWidget(new SideItem(layout.sideX(), layout.sideY(CATEGORIES.length + i), layout.sideWidth(), VORTEX_ITEMS[i][0], VORTEX_ITEMS[i][1],
                () -> "", () -> false, b -> actions[index].run()));
        }
        addRenderableWidget(new SideItem(layout.sideX(), layout.sideSettingsY(), layout.sideWidth(), "settings", "Settings",
            () -> "", () -> settingsPage && selectedModule == null, b -> changePage(true, null)));
    }
    private void changePage(boolean settings, ClientModule module) {
        if (settingsPage == settings && selectedModule == module) return;
        settingsPage = settings; selectedModule = module;
        pageOpened = System.nanoTime();
        rebuildWidgets();
    }
    private void selectFilter(String category) {
        if (!settingsPage && selectedModule == null && filter.equals(category)) return;
        filter = category; resetScroll();
        // Filters update content, not the entrance animation of the entire window.
        settingsPage = false; selectedModule = null;
        rebuildWidgets();
    }
    private int viewportTop() { return layout.y() + 112; }
    private int viewportBottom() { return layout.y() + layout.height() - 34; }
    private int viewportHeight() { return Math.max(1, viewportBottom() - viewportTop()); }
    private int maximumScroll() { return ModuleScroll.maximum(matchingCount, layout.columns(), layout.cardHeight(), viewportHeight()); }
    private void resetScroll() { scroll = scrollTarget = 0; }
    private boolean inViewport(double mx, double my) {
        return mx >= layout.contentX() && mx < layout.contentX() + layout.contentWidth() && my >= viewportTop() && my < viewportBottom();
    }
    private void rebuildCards() {
        for (var widget : cards) removeWidget(widget);
        cards.clear();
        var matches = Arrays.stream(ClientModule.values()).filter(module -> module.matches(query, filter)).toList();
        matchingCount = matches.size();
        scrollTarget = Math.clamp(scrollTarget, 0, maximumScroll()); scroll = Math.clamp(scroll, 0, maximumScroll());
        for (int i = 0; i < matches.size(); i++) {
            ClientModule module = matches.get(i);
            var card = new ModuleCard(layout.contentX() + (i % layout.columns()) * (layout.cardWidth() + 8),
                viewportTop() + (i / layout.columns()) * (layout.cardHeight() + 8),
                layout.cardWidth(), layout.cardHeight(), i, module, () -> {
                    module.toggle(); dirtySettings = true; saveSettings();
                    if (filter.equals("Enabled")) rebuildCards();
                }, () -> changePage(true, module));
            cards.add(addWidget(card)); // Input/narration only; render visible cards inside the scissor.
        }
        updateCardPositions();
    }
    private void updateCardPositions() {
        for (int i = 0; i < cards.size(); i++) {
            var card = (ModuleCard)cards.get(i);
            card.setY(viewportTop() + i / layout.columns() * (layout.cardHeight() + 8) - (int)Math.round(scroll));
            card.clipTop = viewportTop(); card.clipBottom = viewportBottom();
            card.visible = ModuleScroll.intersects(card.getY(), card.getHeight(), viewportTop(), viewportBottom());
            if (!card.visible && getFocused() == card) setFocused(null);
        }
    }
    @Override public boolean mouseScrolled(double mx, double my, double horizontal, double vertical) {
        if (!settingsPage && inViewport(mx, my) && Double.isFinite(vertical) && vertical != 0) {
            scrollTarget = ModuleScroll.wheel(scrollTarget, vertical, maximumScroll()); return true;
        }
        return super.mouseScrolled(mx, my, horizontal, vertical);
    }
    @Override public boolean mouseClicked(MouseButtonEvent event, boolean doubleClick) {
        int right = layout.contentX() + layout.contentWidth() + 4;
        if (!settingsPage && maximumScroll() > 0 && event.button() == InputConstants.MOUSE_BUTTON_LEFT &&
            event.x() >= right - 2 && event.x() < right + 7 && event.y() >= viewportTop() && event.y() < viewportBottom()) {
            int thumb = ModuleScroll.thumbHeight(viewportHeight(), maximumScroll());
            double thumbY = viewportTop() + scroll / maximumScroll() * (viewportHeight() - thumb);
            scrollbarGrab = event.y() >= thumbY && event.y() < thumbY + thumb ? event.y() - thumbY : thumb / 2.0;
            scrollbarDragging = true;
            scroll = scrollTarget = ModuleScroll.drag(event.y() - viewportTop() - scrollbarGrab, viewportHeight(), maximumScroll());
            updateCardPositions(); return true;
        }
        return super.mouseClicked(event, doubleClick);
    }
    @Override public boolean mouseDragged(MouseButtonEvent event, double dx, double dy) {
        if (scrollbarDragging && event.button() == InputConstants.MOUSE_BUTTON_LEFT) {
            scroll = scrollTarget = ModuleScroll.drag(event.y() - viewportTop() - scrollbarGrab, viewportHeight(), maximumScroll());
            updateCardPositions(); return true;
        }
        return super.mouseDragged(event, dx, dy);
    }
    @Override public boolean mouseReleased(MouseButtonEvent event) {
        if (scrollbarDragging && event.button() == InputConstants.MOUSE_BUTTON_LEFT) { scrollbarDragging = false; return true; }
        return super.mouseReleased(event);
    }
    @Override public boolean keyPressed(KeyEvent event) {
        if (!settingsPage && !(getFocused() instanceof EditBox)) {
            switch (event.key()) {
                case InputConstants.KEY_PAGEDOWN -> { scrollTarget = Math.min(maximumScroll(), scrollTarget + viewportHeight()); return true; }
                case InputConstants.KEY_PAGEUP -> { scrollTarget = Math.max(0, scrollTarget - viewportHeight()); return true; }
                case InputConstants.KEY_HOME -> { scrollTarget = 0; return true; }
                case InputConstants.KEY_END -> { scrollTarget = maximumScroll(); return true; }
            }
        }
        return super.keyPressed(event);
    }

    private void initSettings() {
        var help=new ClientUi.ActionButton(layout.x()+layout.width()-59,headerButtonY(),22,22,Component.literal("?"),b->minecraft.gui.setScreen(new ModuleHelpScreen(this,selectedModule)));
        help.setTooltip(Tooltip.create(Component.literal("Explain this module, settings and controls")));addRenderableWidget(help);
        int x = layout.contentX(), y = layout.y() + 66, w = layout.contentWidth();
        ClientModule module = selectedModule;
        if (module == null) {
            int half = (w - 8) / 2;
            addRenderableWidget(new ClientUi.Slider(x, y, half, "Default HUD size", .75, 2, VortexClient.config.hudScale,
                value -> Math.round(value * 100) + "%", value -> { VortexClient.config.hudScale = (float)value; dirtySettings = true; }, this::saveSettings));
            addRenderableWidget(new ClientUi.Slider(x + half + 8, y, half, "Default opacity", .25, 1, VortexClient.config.hudOpacity,
                value -> Math.round(value * 100) + "%", value -> { VortexClient.config.hudOpacity = (float)value; dirtySettings = true; }, this::saveSettings));
            addRenderableWidget(new ClientUi.ActionButton(x, y + 44, half, 26,
                Component.literal(VortexClient.config.uiAnimations ? "Animations: ON" : "Animations: OFF"), b -> {
                    VortexClient.config.uiAnimations = !VortexClient.config.uiAnimations;
                    b.setMessage(Component.literal(VortexClient.config.uiAnimations ? "Animations: ON" : "Animations: OFF"));
                    dirtySettings = true; saveSettings();
                }));
            addRenderableWidget(new ClientUi.ActionButton(x + half + 8, y + 44, half, 26,
                Component.literal(VortexClient.config.clientNotifications ? "Notifications: ON" : "Notifications: OFF"), b -> {
                    VortexClient.config.clientNotifications = !VortexClient.config.clientNotifications;
                    b.setMessage(Component.literal(VortexClient.config.clientNotifications ? "Notifications: ON" : "Notifications: OFF"));
                    dirtySettings = true; saveSettings();
                }));
            // Rows of equal height, 8 px apart: toggles, shortcuts, Discord, then the accent colour.
            addRenderableWidget(new VortexMenuChrome.ShortcutButton(x, y + 78, half, 26, "keybinds", "Key bindings", b -> minecraft.gui.setScreen(new KeybindScreen(this))).raised());
            addRenderableWidget(new VortexMenuChrome.ShortcutButton(x + half + 8, y + 78, half, 26, "hud", "HUD Editor", b -> minecraft.gui.setScreen(new HudEditorScreen(this))).raised());
            int utilityWidth=VortexClient.config.performanceBackup==null?w:(w-8)/2;
            addRenderableWidget(new VortexMenuChrome.ShortcutButton(x, y + 112, utilityWidth, 26, "social", "Discord", b -> minecraft.gui.setScreen(new DiscordPresenceScreen(this))).raised());
            if (VortexClient.config.performanceBackup != null)
                addRenderableWidget(new ClientUi.ActionButton(x + utilityWidth + 8, y + 112, utilityWidth, 26, Component.literal("Restore"), b -> {
                    saveMessage = PerformanceProfile.restore(minecraft); rebuildWidgets();
                }));
            // Menu panorama: the title-screen background from an older Minecraft version.
            if (panoramaRowFits()) {
                var panorama = new VortexMenuChrome.ShortcutButton(x, y + 184, w, 26, "world", "Menu panorama: " + PanoramaLibrary.title(VortexClient.config.menuPanorama),
                    b -> minecraft.gui.setScreen(new PanoramaScreen(this))).raised();
                addRenderableWidget(panorama);
            }
            // Accent colour: the whole client interface follows it at once.
            for (int i = 0; i < ClientUi.ACCENTS.length && accentRowFits(); i++) {
                final int color = ClientUi.ACCENTS[i];
                addRenderableWidget(new Swatch(x + 92 + i * 24, y + 150, color, ClientUi.ACCENT_NAMES[i],
                    () -> VortexClient.config.clientAccent == color, b -> {
                        VortexClient.config.clientAccent = color; ClientUi.applyAccent(color); dirtySettings = true; saveSettings();
                    }));
            }
            return;
        }
        if (module == ClientModule.CROSSHAIR) { initCrosshair(x, y, w); return; }
        if (module != null) {
            var enable = new ClientUi.ActionButton(x, y, module == ClientModule.ARMOR ? (w - 8) / 3 : module == ClientModule.CROSSHAIR ? w / 2 - 4 : w, 24,
                Component.literal(module.enabled() ? "Enabled" : "Disabled"), button -> {
                    module.toggle();
                    dirtySettings = true;
                    button.setMessage(Component.literal(module.enabled() ? "Enabled" : "Disabled"));
                    saveSettings();
                });
            addRenderableWidget(enable);
            if (module == ClientModule.CROSSHAIR) {
                int toggleWidth = w / 4 - 4;
                var outline = new ClientUi.ActionButton(x + w / 2, y, toggleWidth, 24,
                    Component.literal(VortexClient.config.crosshairOutline ? "Edge: ON" : "Edge: OFF"), b -> {
                        VortexClient.config.crosshairOutline = !VortexClient.config.crosshairOutline;
                        b.setMessage(Component.literal(VortexClient.config.crosshairOutline ? "Edge: ON" : "Edge: OFF"));
                        dirtySettings = true; saveSettings();
                    });
                outline.setTooltip(Tooltip.create(Component.literal("Black outline for readability")));
                addRenderableWidget(outline);
                var dot = new ClientUi.ActionButton(x + w * 3 / 4, y, toggleWidth, 24,
                    Component.literal(VortexClient.config.crosshairDot ? "Dot: ON" : "Dot: OFF"), b -> {
                        VortexClient.config.crosshairDot = !VortexClient.config.crosshairDot;
                        b.setMessage(Component.literal(VortexClient.config.crosshairDot ? "Dot: ON" : "Dot: OFF"));
                        dirtySettings = true; saveSettings();
                    });
                dot.setTooltip(Tooltip.create(Component.literal("Center dot")));
                addRenderableWidget(dot);
            }
            if (module == ClientModule.ARMOR) {
                int third = (w - 8) / 3;
                var items = new ClientUi.ActionButton(x + third + 4, y, third, 24,
                    Component.literal(VortexClient.config.heldItemEnabled ? "Items: ON" : "Items: OFF"), b -> {
                        VortexClient.config.heldItemEnabled = !VortexClient.config.heldItemEnabled;
                        b.setMessage(Component.literal(VortexClient.config.heldItemEnabled ? "Items: ON" : "Items: OFF"));
                        dirtySettings = true; saveSettings();
                    });
                items.setTooltip(Tooltip.create(Component.literal("Show held item durability alongside armor")));
                addRenderableWidget(items);
                var offhand = new ClientUi.ActionButton(x + (third + 4) * 2, y, third, 24,
                    Component.literal(VortexClient.config.heldOffhand ? "Off: ON" : "Off: OFF"), b -> {
                        VortexClient.config.heldOffhand = !VortexClient.config.heldOffhand;
                        b.setMessage(Component.literal(VortexClient.config.heldOffhand ? "Off: ON" : "Off: OFF"));
                        dirtySettings = true; saveSettings();
                    });
                offhand.setTooltip(Tooltip.create(Component.literal("Include damageable offhand items")));
                addRenderableWidget(offhand);
            }
            y += 30;
        }
        if (module == ClientModule.CROSSHAIR) {
            int half = (w - 8) / 2;
            addRenderableWidget(new ClientUi.Slider(x, y, half, "Size", 2, 12, VortexClient.config.crosshairSize,
                value -> Integer.toString((int)Math.round(value)), value -> { VortexClient.config.crosshairSize = (int)Math.round(value); dirtySettings = true; }, this::saveSettings));
            addRenderableWidget(new ClientUi.Slider(x + half + 8, y, half, "Gap", 0, 8, VortexClient.config.crosshairGap,
                value -> Integer.toString((int)Math.round(value)), value -> { VortexClient.config.crosshairGap = (int)Math.round(value); dirtySettings = true; }, this::saveSettings));
            addRenderableWidget(new ClientUi.Slider(x, y + 40, half, "Thickness", 1, 4, VortexClient.config.crosshairThickness,
                value -> Integer.toString((int)Math.round(value)), value -> { VortexClient.config.crosshairThickness = (int)Math.round(value); dirtySettings = true; }, this::saveSettings));
            addRenderableWidget(new ClientUi.Slider(x + half + 8, y + 40, half, "Opacity", .25, 1, VortexClient.config.crosshairOpacity,
                value -> Math.round(value * 100) + "%", value -> { VortexClient.config.crosshairOpacity = (float)value; dirtySettings = true; }, this::saveSettings));
            addRenderableWidget(new ClientUi.ActionButton(x, y + 80, half, 22, Component.literal("Shape: " + CustomCrosshair.shapeName(VortexClient.config.crosshairShape)), b -> {
                VortexClient.config.crosshairShape = (VortexClient.config.crosshairShape + 1) % 3;
                b.setMessage(Component.literal("Shape: " + CustomCrosshair.shapeName(VortexClient.config.crosshairShape))); dirtySettings = true; saveSettings();
            }));
            addRenderableWidget(new ClientUi.ActionButton(x + half + 8, y + 80, half, 22, Component.literal("Color: " + CustomCrosshair.colorName(VortexClient.config.crosshairColor)), b -> {
                VortexClient.config.crosshairColor = CustomCrosshair.nextColor(VortexClient.config.crosshairColor);
                b.setMessage(Component.literal("Color: " + CustomCrosshair.colorName(VortexClient.config.crosshairColor))); dirtySettings = true; saveSettings();
            }));
        } else if (module == ClientModule.RESOURCE_COUNTER) {
            addRenderableWidget(new ClientUi.ActionButton(x,y,w,24,Component.literal("Manage resource goals"),b->minecraft.gui.setScreen(new ResourceGoalsScreen(this)),true));
            addRenderableWidget(new ClientUi.ActionButton(x,y+32,w,24,Component.literal("Position in HUD Editor"),b->minecraft.gui.setScreen(new HudEditorScreen(this))));
        } else if (module == ClientModule.STORAGE_MEMORY) {
            addRenderableWidget(new ClientUi.ActionButton(x,y,w,24,Component.literal("Search remembered storage"),b->minecraft.gui.setScreen(new StorageFinderScreen(this)),true));
            addRenderableWidget(new ClientUi.Slider(x,y+34,w,"Nearby radius",16,256,VortexClient.config.storageMemoryRadius,value->Math.round(value)+" m",value->{VortexClient.config.storageMemoryRadius=(int)Math.round(value);dirtySettings=true;},this::saveSettings));
        } else if (module == ClientModule.CHAT) {
            addRenderableWidget(new ClientUi.ActionButton(x, y, w, 26,
                Component.literal(VortexClient.config.chatTimestamps ? "Timestamps: ON" : "Timestamps: OFF"), b -> {
                    VortexClient.config.chatTimestamps = !VortexClient.config.chatTimestamps;
                    b.setMessage(Component.literal(VortexClient.config.chatTimestamps ? "Timestamps: ON" : "Timestamps: OFF"));
                    dirtySettings = true; saveSettings();
                }));
            addRenderableWidget(new ClientUi.ActionButton(x, y + 34, w, 26,
                Component.literal(VortexClient.config.chatCollapseRepeats ? "Collapse repeats: ON" : "Collapse repeats: OFF"), b -> {
                    VortexClient.config.chatCollapseRepeats = !VortexClient.config.chatCollapseRepeats;
                    b.setMessage(Component.literal(VortexClient.config.chatCollapseRepeats ? "Collapse repeats: ON" : "Collapse repeats: OFF"));
                    dirtySettings = true; saveSettings();
                }));
        } else if (module == ClientModule.WAYPOINTS) {
            addRenderableWidget(new ClientUi.ActionButton(x,y,w,24,Component.literal("Open waypoints"),b->WaypointsFeature.open(this,false),true));
            addRenderableWidget(new ClientUi.ActionButton(x,y+34,w,24,Component.literal("Key: "+ClientBindings.label(VortexClient.config.waypointsKey)),b->minecraft.gui.setScreen(new KeybindScreen(this,ClientBindings.Action.WAYPOINTS))));
        } else if (module == ClientModule.COMPACT_SHIELD) {
            addRenderableWidget(new ClientUi.Slider(x,y,w,"Shield size",.5,1,VortexClient.config.shieldScale,value->Math.round(value*100)+"%",value->{VortexClient.config.shieldScale=(float)value;dirtySettings=true;},this::saveSettings));
        } else if (module == ClientModule.ENTITY_HEARTS) {
            addRenderableWidget(new ClientUi.ActionButton(x,y,w,24,Component.literal("Players: "+(VortexClient.config.entityHeartsPlayers?"ON":"OFF")),b->{VortexClient.config.entityHeartsPlayers=!VortexClient.config.entityHeartsPlayers;b.setMessage(Component.literal("Players: "+(VortexClient.config.entityHeartsPlayers?"ON":"OFF")));dirtySettings=true;saveSettings();}));
            addRenderableWidget(new ClientUi.ActionButton(x,y+32,w,24,Component.literal("Mobs: "+(VortexClient.config.entityHeartsMobs?"ON":"OFF")),b->{VortexClient.config.entityHeartsMobs=!VortexClient.config.entityHeartsMobs;b.setMessage(Component.literal("Mobs: "+(VortexClient.config.entityHeartsMobs?"ON":"OFF")));dirtySettings=true;saveSettings();}));
            addRenderableWidget(new ClientUi.Slider(x,y+70,w,"Visible range",8,64,VortexClient.config.entityHeartsRange,value->Math.round(value)+" m",value->{VortexClient.config.entityHeartsRange=(int)Math.round(value);dirtySettings=true;},this::saveSettings));
        } else if (module == ClientModule.FULLBRIGHT || module == ClientModule.CHEST_SEARCH || module == ClientModule.SHULKER_PREVIEW || module == ClientModule.ENCHANTMENT_HINTS || module == ClientModule.DEATH_WAYPOINTS || module == ClientModule.PLAYER_LIST) {
            // Only enable/disable; this is not a HUD overlay.
        } else if (module == ClientModule.FREELOOK) {
            addRenderableWidget(new ClientUi.Slider(x, y, w, "Look sensitivity", .25, 2,
                VortexClient.config.freelookSensitivity, value -> Math.round(value * 100) + "%",
                value -> { VortexClient.config.freelookSensitivity = (float)value; dirtySettings = true; }, this::saveSettings));
            addRenderableWidget(new ClientUi.ActionButton(x, y + 44, w, 24,
                Component.literal("Hold key: " + ClientBindings.label(VortexClient.config.freelookKey)), b ->
                    minecraft.gui.setScreen(new KeybindScreen(this, ClientBindings.Action.FREELOOK))));
        } else if (module == ClientModule.ZOOM) {
            addRenderableWidget(new ClientUi.Slider(x, y, w, "Zoom strength", 2, VortexClient.MAX_ZOOM,
                VortexClient.config.zoomMultiplier, value -> String.format(Locale.ROOT, "%.1fx", value),
                value -> { VortexClient.config.zoomMultiplier = (float)value; dirtySettings = true; }, this::saveSettings));
            addRenderableWidget(new ClientUi.ActionButton(x, y + 44, w, 24,
                Component.literal((VortexClient.config.zoomToggle ? "Toggle key: " : "Hold key: ") + ClientBindings.label(VortexClient.config.zoomKey)), b ->
                    minecraft.gui.setScreen(new KeybindScreen(this, ClientBindings.Action.ZOOM))));
            int half = (w - 8) / 2;
            addRenderableWidget(new ClientUi.ActionButton(x, y + 76, half, 24,
                Component.literal(VortexClient.config.zoomToggle ? "Mode: press to toggle" : "Mode: hold key"), b -> {
                    VortexClient.config.zoomToggle = !VortexClient.config.zoomToggle; dirtySettings = true; saveSettings(); rebuildWidgets();
                }));
            addRenderableWidget(new ClientUi.ActionButton(x + half + 8, y + 76, half, 24,
                Component.literal(VortexClient.config.zoomSlowMouse ? "Slower mouse: ON" : "Slower mouse: OFF"), b -> {
                    VortexClient.config.zoomSlowMouse = !VortexClient.config.zoomSlowMouse;
                    b.setMessage(Component.literal(VortexClient.config.zoomSlowMouse ? "Slower mouse: ON" : "Slower mouse: OFF")); dirtySettings = true; saveSettings();
                }));
        } else {
            HudModules.Element element = module == null ? null : module.hud;
            addRenderableWidget(new ClientUi.Slider(x, y, w, element == null ? "Default HUD size" : "Size", .75, 2,
                element == null ? VortexClient.config.hudScale : HudModules.scale(element),
                value -> Math.round(value * 100) + "%", value -> {
                    if (element == null) VortexClient.config.hudScale = (float)value;
                    else HudModules.appearance(element, (float)value, HudModules.opacity(element));
                    dirtySettings = true;
                }, this::saveSettings));
            addRenderableWidget(new ClientUi.Slider(x, y + 40, w, element == null ? "Default opacity" : "Opacity", .25, 1,
                element == null ? VortexClient.config.hudOpacity : HudModules.opacity(element),
                value -> Math.round(value * 100) + "%", value -> {
                    if (element == null) VortexClient.config.hudOpacity = (float)value;
                    else HudModules.appearance(element, HudModules.scale(element), (float)value);
                    dirtySettings = true;
                }, this::saveSettings));
            if (module == ClientModule.ARMOR) {
                addRenderableWidget(new ClientUi.ActionButton(x, y + 80, (w - 8) / 2, 22,
                    Component.literal(VortexClient.config.armorHorizontal ? "Layout: Horizontal" : "Layout: Vertical"), button -> {
                        VortexClient.config.armorHorizontal = !VortexClient.config.armorHorizontal;
                        HudModules.keepOnScreen(HudModules.Element.ARMOR, width, height);
                        button.setMessage(Component.literal(VortexClient.config.armorHorizontal ? "Layout: Horizontal" : "Layout: Vertical"));
                        dirtySettings = true; saveSettings();
                    }));
                addRenderableWidget(new ClientUi.ActionButton(x + (w + 8) / 2, y + 80, (w - 8) / 2, 22,
                    Component.literal("Durability alerts"), b -> minecraft.gui.setScreen(new DurabilitySettingsScreen(this))));
            }
        }
    }

    /** The accent row sits under the settings; it is left out only when the window is too short or narrow for it. */
    private boolean accentRowFits() {
        return layout.y() + 66 + 150 + 20 <= layout.y() + layout.height() - 34 && 92 + ClientUi.ACCENTS.length * 24 <= layout.contentWidth();
    }
    private boolean panoramaRowFits() { return layout.y() + 66 + 184 + 26 <= layout.y() + layout.height() - 34; }
    /** Width of the live crosshair preview beside the form; 0 when the window is too narrow. */
    static int crosshairPreviewWidth(int contentWidth) { return contentWidth >= 360 ? Math.min(150, contentWidth / 3) : 0; }
    private void initCrosshair(int x, int y, int w) {
        int preview = crosshairPreviewWidth(w);
        int fw = preview > 0 ? w - preview - 12 : w;
        var c = VortexClient.config;
        var enable = new ClientUi.ActionButton(x, y, fw, 22, Component.literal(ClientModule.CROSSHAIR.enabled() ? "Enabled" : "Disabled"), b -> {
            ClientModule.CROSSHAIR.toggle(); dirtySettings = true;
            b.setMessage(Component.literal(ClientModule.CROSSHAIR.enabled() ? "Enabled" : "Disabled")); saveSettings();
        });
        addRenderableWidget(enable);
        // Shape: a segmented control instead of a cycling button.
        int segment = (fw - 8) / 3;
        for (int i = 0; i < 3; i++) {
            final int shape = i;
            addRenderableWidget(new FilterPill(x + i * (segment + 4), y + 28, segment, 20, Component.literal(CustomCrosshair.shapeName(i)),
                () -> c.crosshairShape == shape, b -> { c.crosshairShape = shape; dirtySettings = true; saveSettings(); }));
        }
        int[] colors = CustomCrosshair.palette();
        for (int i = 0; i < colors.length; i++) {
            final int color = colors[i];
            addRenderableWidget(new Swatch(x + i * 24, y + 54, color, CustomCrosshair.colorName(color),
                () -> c.crosshairColor == color, b -> { c.crosshairColor = color; dirtySettings = true; saveSettings(); }));
        }
        int half = (fw - 8) / 2;
        addRenderableWidget(new ClientUi.Slider(x, y + 80, half, "Size", 2, 12, c.crosshairSize,
            value -> Integer.toString((int)Math.round(value)), value -> { c.crosshairSize = (int)Math.round(value); dirtySettings = true; }, this::saveSettings));
        addRenderableWidget(new ClientUi.Slider(x + half + 8, y + 80, half, "Gap", 0, 8, c.crosshairGap,
            value -> Integer.toString((int)Math.round(value)), value -> { c.crosshairGap = (int)Math.round(value); dirtySettings = true; }, this::saveSettings));
        addRenderableWidget(new ClientUi.Slider(x, y + 118, half, "Thickness", 1, 4, c.crosshairThickness,
            value -> Integer.toString((int)Math.round(value)), value -> { c.crosshairThickness = (int)Math.round(value); dirtySettings = true; }, this::saveSettings));
        addRenderableWidget(new ClientUi.Slider(x + half + 8, y + 118, half, "Opacity", .25, 1, c.crosshairOpacity,
            value -> Math.round(value * 100) + "%", value -> { c.crosshairOpacity = (float)value; dirtySettings = true; }, this::saveSettings));
        var outline = new FilterPill(x, y + 156, half, 20, Component.literal("Outline"), () -> c.crosshairOutline,
            b -> { c.crosshairOutline = !c.crosshairOutline; dirtySettings = true; saveSettings(); });
        outline.setTooltip(Tooltip.create(Component.literal("Black outline for readability")));
        addRenderableWidget(outline);
        var dot = new FilterPill(x + half + 8, y + 156, half, 20, Component.literal("Center dot"), () -> c.crosshairDot,
            b -> { c.crosshairDot = !c.crosshairDot; dirtySettings = true; saveSettings(); });
        addRenderableWidget(dot);
        if (preview > 0) {
            int px = x + w - preview, bottom = y + 182;
            addRenderableWidget(new ClientUi.ActionButton(px, bottom - 22, (preview - 4) / 2, 22, Component.literal("Reset"), b -> {
                CustomCrosshair.reset(c); dirtySettings = true; saveSettings(); rebuildWidgets();
            }));
            addRenderableWidget(new ClientUi.ActionButton(px + (preview + 4) / 2, bottom - 22, (preview - 4) / 2, 22, Component.literal("Done"),
                b -> changePage(false, null), true));
        }
    }
    /** Live preview: the real crosshair, magnified over a soft sky-and-grass backdrop. */
    private void drawCrosshairPreview(GuiGraphicsExtractor g) {
        int w = layout.contentWidth(), preview = crosshairPreviewWidth(w);
        if (preview == 0) return;
        int px = layout.contentX() + w - preview, py = layout.y() + 66, ph = 182 - 30;
        ClientUi.rounded(g, px, py, preview, ph, 12, 0xFF3B4A6B);
        ClientUi.rounded(g, px, py + ph / 2 + 18, preview, ph / 2 - 18, 12, 0xFF3F5A3A);
        g.fill(px, py + ph / 2 + 18, px + preview, py + ph / 2 + 30, 0xFF3F5A3A);
        ClientUi.eyebrow(g, "Live preview", px + 10, py + 8, preview - 20);
        int scale = 3;
        g.pose().pushMatrix();
        g.pose().translate(px + preview / 2f, py + ph / 2f + 4);
        g.pose().scale(scale, scale);
        CustomCrosshair.draw(g, 0, 0);
        g.pose().popMatrix();
    }
    /** Round colour swatch; the chosen one carries an accent ring. */
    static final class Swatch extends ClientUi.ActionButton {
        private final int color;
        private final java.util.function.BooleanSupplier selected;
        private final UiMotion.Feedback motion = new UiMotion.Feedback();
        Swatch(int x, int y, int color, String name, java.util.function.BooleanSupplier selected, OnPress press) {
            super(x, y, 20, 20, Component.literal(name), press);
            this.color = color; this.selected = selected;
            setTooltip(Tooltip.create(Component.literal(name)));
        }
        @Override protected void extractContents(GuiGraphicsExtractor g, int mx, int my, float delta) {
            motion.frame(isHoveredOrFocused(), selected.getAsBoolean());
            UiMotion.begin(g, getX(), getY(), getWidth(), getHeight(), pressScale(System.nanoTime()), 0);
            int ring = ClientUi.mix(ClientUi.mix(0x00000000, ClientUi.BORDER, motion.hover), ClientUi.ACCENT, motion.selected);
            ClientUi.rounded(g, getX(), getY(), 20, 20, 10, ring);
            ClientUi.rounded(g, getX() + 3, getY() + 3, 14, 14, 7, 0xFF000000 | color);
            g.pose().popMatrix();
        }
    }
    private boolean saveSettings() {
        if (!dirtySettings) return true;
        if (VortexClient.config.save()) {
            dirtySettings = false; saveMessage = "Settings saved"; return true;
        }
        saveMessage = "Could not save. See game log."; return false;
    }

    @Override public boolean isInGameUi() { return minecraft.level != null; }
    @Override public void extractBackground(GuiGraphicsExtractor g, int mx, int my, float delta) {
        if (isInGameUi()) minecraft.gui.hud.extractDeferredSubtitles();
        else extractPanorama(g, delta);
    }
    @Override public void extractRenderState(GuiGraphicsExtractor g, int mx, int my, float delta) {
        float progress = UiMotion.progress(opened);
        int alpha = (int)(170 * (1 - Math.pow(1 - progress, 3))) << 24;
        if (!settingsPage) g.fill(0, 0, width, height, alpha | 0x08090F);
        int x = layout.x(), y = layout.y(), w = layout.width(), h = layout.height();
        float entrance = UiMotion.progress(pageOpened);
        String title = !settingsPage ? "Modules" : selectedModule == null ? "Client settings" : selectedModule.title;
        if (settingsPage) ClientUi.beginPage(g, width, height, x, y, w, h, title,
            selectedModule == null ? "settings" : selectedModule.name().toLowerCase(Locale.ROOT), pageOpened);
        else {
            // The module window grows out of the centre while it fades up.
            UiMotion.begin(g, x, y, w, h, .96f + .04f * entrance, (1 - entrance) * 4);
            ClientUi.rounded(g, x - 2, y + 4, w + 4, h + 2, 12, 0x40000000);
            ClientUi.rounded(g, x, y, w, h, 12, ClientUi.GLASS);
            ClientUi.rounded(g, x + 11, y + 9, 26, 26, 12, ClientUi.ACCENT_SOFT);
            ClientUi.icon(g, "brand", x + 14, y + 12, 20, ClientUi.TEXT);
            if (layout.sidebar() > 0) {
                ClientUi.heading(g, "Vortex", x + 44, y + 16, layout.sidebar() - 44);
                ClientUi.heading(g, title, layout.contentX(), y + 16, layout.contentWidth() - 130);
            } else ClientUi.heading(g, title, layout.contentX() + 29, y + 14, layout.contentWidth() - 60);
        }
        if (layout.sidebar() > 0) {
            // A quieter strip behind the navigation; settings pages draw it over their own panel.
            ClientUi.rounded(g, x + 6, y + 42, layout.sidebar(), h - 48, 10, 0x40000000);
            ClientUi.eyebrow(g, "Categories", layout.sideX() + 6, layout.sideY(0) - 13, layout.sideWidth() - 12);
            ClientUi.eyebrow(g, "Vortex", layout.sideX() + 6, layout.sideY(CATEGORIES.length) - 13, layout.sideWidth() - 12);
        }
        String subtitle = !settingsPage ? "Make it yours" : selectedModule == null ? "Appearance, notifications and controls" : selectedModule.description;
        if (settingsPage) ClientUi.text(g, subtitle, layout.contentX(), y + 42, layout.contentWidth(), ClientUi.MUTED);
        if (settingsPage && selectedModule == null && accentRowFits())
            ClientUi.text(g, "Accent colour", layout.contentX(), y + 66 + 156, 88, ClientUi.MUTED);
        if (settingsPage && selectedModule == ClientModule.CROSSHAIR) {
            drawCrosshairPreview(g);
            ClientUi.eyebrow(g, "Color", layout.contentX() + 150, y + 66 + 60, 60);
        }
        if (!settingsPage) {
            ClientUi.pill(g, layout.contentX(), y + searchY(), layout.contentWidth(), 28, 0xFF252833);
            ClientUi.icon(g, "search", layout.contentX() + 8, y + searchY() + 7, 15, ClientUi.MUTED);
            if (layout.sidebar() > 0) {
                String heading = filter.equals("All Modules") ? "All modules" : filter;
                ClientUi.text(g, heading, layout.contentX() + 2, y + 92, layout.contentWidth() / 2, ClientUi.TEXT);
                String shown = matchingCount + " of " + ClientModule.values().length + " shown";
                var bodyFont = UiFonts.body();
                ClientUi.text(g, shown, layout.contentX() + layout.contentWidth() - bodyFont.width(shown) - 2, y + 92, layout.contentWidth() / 2, ClientUi.MUTED);
            }
            if (matchingCount == 0) {
                ClientUi.text(g, "No modules found", layout.contentX() + 8, y + 117, layout.contentWidth() - 16, ClientUi.TEXT);
                ClientUi.text(g, "Try another search or category.", layout.contentX() + 8, y + 135, layout.contentWidth() - 16, ClientUi.MUTED);
            }
        }
        int saveOffset = layout.sidebar() > 0 ? 0 : 178;
        if (settingsPage && w >= 500) ClientUi.text(g, saveMessage, layout.contentX() + saveOffset, y + h - 21, layout.contentWidth() - saveOffset, ClientUi.MUTED);
        if (!settingsPage) {
            if (layout.sidebar() == 0) ClientUi.text(g, matchingCount + " modules", x + w - 80, y + h - 21, 65, ClientUi.MUTED);
            long now = System.nanoTime();
            scroll = VortexClient.config.uiAnimations && !scrollbarDragging ?
                scroll + (scrollTarget - scroll) * (1 - Math.exp(-Math.clamp((now - scrollFrame) / 1e9, 0, .1) * 18)) : scrollTarget;
            scrollFrame = now;
            if (Math.abs(scroll - scrollTarget) < .05) scroll = scrollTarget;
            updateCardPositions();
            g.enableScissor(layout.contentX(), viewportTop(), layout.contentX() + layout.contentWidth(), viewportBottom());
            for (var card : cards) if (card.visible) card.extractRenderState(g, mx, my, delta);
            g.disableScissor();
            if (maximumScroll() > 0) {
                int right = layout.contentX() + layout.contentWidth() + 4;
                int thumb = ModuleScroll.thumbHeight(viewportHeight(), maximumScroll());
                int thumbY = viewportTop() + (int)Math.round(scroll / maximumScroll() * (viewportHeight() - thumb));
                ClientUi.rounded(g, right, viewportTop(), 3, viewportHeight(), 1, ClientUi.BORDER);
                ClientUi.rounded(g, right, thumbY, 3, thumb, 1, ClientUi.ACCENT);
            }
        }
        super.extractRenderState(g, mx, my, delta);
        g.pose().popMatrix();
    }

    @Override public void onClose() { if (saveSettings()) minecraft.gui.setScreen(parent); }
    @Override public void removed() { saveSettings(); }
    @Override public boolean isPauseScreen() { return false; }

    static final class VortexButton extends ClientUi.ActionButton {
        VortexButton(int x, int y, int w, int h, Component label, OnPress press) { super(x, y, w, h, label, press); }
    }

    /** Sidebar row: icon, label and an optional count; the selected row is filled with the soft accent. */
    static final class SideItem extends ClientUi.ActionButton {
        private final String icon;
        private final java.util.function.Supplier<String> count;
        private final java.util.function.BooleanSupplier selected;
        private final UiMotion.Feedback motion = new UiMotion.Feedback();
        SideItem(int x, int y, int w, String icon, String label, java.util.function.Supplier<String> count,
                 java.util.function.BooleanSupplier selected, OnPress press) {
            super(x, y, w, SIDE_ROW, Component.literal(label), press);
            this.icon = icon; this.count = count; this.selected = selected;
        }
        @Override protected void extractContents(GuiGraphicsExtractor g, int mx, int my, float delta) {
            boolean chosen = selected.getAsBoolean();
            motion.frame(isHoveredOrFocused(), chosen);
            UiMotion.begin(g, getX(), getY(), getWidth(), getHeight(), pressScale(System.nanoTime()), 0);
            int background = ClientUi.mix(ClientUi.mix(0x00000000, 0xFF2B2E3A, motion.hover), ClientUi.ACCENT_TINT, motion.selected);
            ClientUi.pill(g, getX(), getY(), getWidth(), getHeight(), background);
            int color = ClientUi.mix(ClientUi.MUTED, ClientUi.TEXT, Math.max(motion.hover, motion.selected));
            ClientUi.icon(g, icon, getX() + 7, getY() + (getHeight() - 12) / 2, 12, ClientUi.mix(color, ClientUi.ACCENT, motion.selected));
            var font = UiFonts.body();
            String number = count.get();
            int numberWidth = number.isEmpty() ? 0 : font.width(number) + 8;
            ClientUi.text(g, getMessage().getString(), getX() + 25, getY() + (getHeight() - 9) / 2, getWidth() - 31 - numberWidth, color);
            if (!number.isEmpty()) g.text(font, number, getX() + getWidth() - font.width(number) - 8, getY() + (getHeight() - 9) / 2, ClientUi.mix(ClientUi.MUTED, ClientUi.ACCENT, motion.selected), false);
            g.pose().popMatrix();
        }
    }
    /** Category pill: the selected one is filled with the soft accent, the rest stay quiet. */
    static final class FilterPill extends ClientUi.ActionButton {
        private final java.util.function.BooleanSupplier selected;
        private final UiMotion.Feedback motion = new UiMotion.Feedback();
        FilterPill(int x, int y, int w, int h, Component label, java.util.function.BooleanSupplier selected, OnPress press) {
            super(x, y, w, h, label, press); this.selected = selected;
        }
        @Override protected void extractContents(GuiGraphicsExtractor g, int mx, int my, float delta) {
            boolean chosen = selected.getAsBoolean();
            motion.frame(isHoveredOrFocused(), chosen);
            UiMotion.begin(g, getX(), getY(), getWidth(), getHeight(), pressScale(System.nanoTime()), 0);
            int background = ClientUi.mix(ClientUi.mix(0x00000000, 0xFF2B2E3A, motion.hover), ClientUi.ACCENT_TINT, motion.selected);
            ClientUi.pill(g, getX(), getY(), getWidth(), getHeight(), background);
            var font = UiFonts.body();
            String label = font.plainSubstrByWidth(getMessage().getString(), getWidth() - 10);
            g.text(font, label, getX() + (getWidth() - font.width(label)) / 2, getY() + (getHeight() - 9) / 2,
                ClientUi.mix(ClientUi.MUTED, ClientUi.TEXT, Math.max(motion.hover, motion.selected)), false);
            g.pose().popMatrix();
        }
    }

    static final class ModuleCard extends Button {
        private final UiMotion.Feedback motion=new UiMotion.Feedback();
        private int clipTop = Integer.MIN_VALUE, clipBottom = Integer.MAX_VALUE;
        private final ClientModule module;
        private final Runnable configure;
        private float hover, state;
        private long previousFrame = System.nanoTime();
        private final long born;
        ModuleCard(int x, int y, int w, int h, ClientModule module, Runnable toggle, Runnable configure) {
            this(x, y, w, h, 0, module, toggle, configure);
        }
        ModuleCard(int x, int y, int w, int h, int index, ClientModule module, Runnable toggle, Runnable configure) {
            super(x, y, w, h, Component.literal(module.title), button -> toggle.run(), DEFAULT_NARRATION);
            this.module = module; this.configure = configure;
            // Cards appear one after another, capped so long lists never wait.
            born = System.nanoTime() + Math.min(index, 10) * 25_000_000L;
            state = module.enabled() ? 1 : 0;
            setTooltip(Tooltip.create(Component.literal(module.description +
                "\nClick to toggle. Right-click, the settings icon, or R to configure.")));
        }
        @Override public boolean mouseClicked(MouseButtonEvent event, boolean doubleClick) {
            return event.y() >= clipTop && event.y() < clipBottom && super.mouseClicked(event, doubleClick);
        }
        @Override public boolean isMouseOver(double mx, double my) {
            return my >= clipTop && my < clipBottom && super.isMouseOver(mx, my);
        }
        @Override protected boolean isValidClickButton(MouseButtonInfo button) {
            return button.button() == InputConstants.MOUSE_BUTTON_LEFT || button.button() == InputConstants.MOUSE_BUTTON_RIGHT;
        }
        @Override public void onClick(MouseButtonEvent event, boolean doubleClick) {
            motion.press();
            if (event.button() == InputConstants.MOUSE_BUTTON_RIGHT ||
                event.x() >= getX() + getWidth() - 29 && event.y() < getY() + 33) configure.run();
            else super.onClick(event, doubleClick);
        }
        @Override public void onPress(net.minecraft.client.input.InputWithModifiers input){motion.press();super.onPress(input);}
        @Override public boolean keyPressed(KeyEvent event) {
            if (isFocused() && event.key() == InputConstants.KEY_R) { configure.run(); return true; }
            return super.keyPressed(event);
        }
        @Override protected void extractContents(GuiGraphicsExtractor g, int mx, int my, float delta) {
            long now = System.nanoTime();
            double seconds = (now - previousFrame) / 1e9;
            previousFrame = now;
            hover = ClientUi.approach(hover, isHoveredOrFocused() ? 1 : 0, seconds);
            state = ClientUi.approach(state, module.enabled() ? 1 : 0, seconds);
            int x = getX(), y = getY(), w = getWidth(), h = getHeight();
            // Hover changes color only: lifting the first row would cross the viewport scissor.
            // The entrance slides down from above for the same reason.
            float entrance = UiMotion.progress(born);
            UiMotion.begin(g, x, y, w, h, motion.scale(), (entrance - 1) * 6);
            int ring = ClientUi.mix(ClientUi.mix(ClientUi.SURFACE, ClientUi.ACCENT_TINT_STRONG, state), ClientUi.ACCENT, hover * .5f);
            int fill = ClientUi.mix(ClientUi.mix(ClientUi.SURFACE, ClientUi.ACCENT_FAINT, state), 0xFF2B2E3A, hover);
            ClientUi.rounded(g, x, y, w, h, 12, VortexMenuChrome.withAlpha(ring, entrance));
            ClientUi.rounded(g, x + 1, y + 1, w - 2, h - 2, 11, VortexMenuChrome.withAlpha(fill, entrance));
            ClientUi.rounded(g, x + 9, y + 10, 26, 26, 9, VortexMenuChrome.withAlpha(ClientUi.mix(0xFF2E3140, ClientUi.ACCENT, state), entrance));
            ClientUi.moduleIcon(g, module, x + 10, y + 11, VortexMenuChrome.withAlpha(ClientUi.mix(ClientUi.TEXT, ClientUi.ACCENT_TEXT, state), entrance), 0);
            ClientUi.icon(g, "settings", x + w - 19, y + 6, 12, VortexMenuChrome.withAlpha(ClientUi.mix(ClientUi.MUTED, ClientUi.TEXT, hover), entrance));
            var font = UiFonts.body();
            String name = font.plainSubstrByWidth(module.title, w - 75);
            g.text(font, name, x + 44, y + 12, VortexMenuChrome.withAlpha(ClientUi.TEXT, entrance), false);
            String description=font.plainSubstrByWidth(module.description,w-58);
            g.text(font,description,x+44,y+28,VortexMenuChrome.withAlpha(ClientUi.MUTED, entrance),false);
            String status = module.enabled() ? "Enabled" : "Disabled";
            g.text(font, status, x + 44, y + h - 16, VortexMenuChrome.withAlpha(ClientUi.mix(ClientUi.MUTED, ClientUi.ACCENT, state), entrance), false);
            ClientUi.toggle(g, x + w - 36, y + h - 20, state);
            g.pose().popMatrix();
        }
    }
}
