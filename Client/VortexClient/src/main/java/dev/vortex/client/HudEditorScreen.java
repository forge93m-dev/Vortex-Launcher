package dev.vortex.client;

import java.util.Locale;
import java.util.ArrayList;
import java.util.List;
import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;

public final class HudEditorScreen extends Screen {
    private final Screen parent;
    private HudModules.Element dragging, resizing, selected;
    private double offsetX, offsetY, resizeX, resizeY;
    private int initialWidth, initialHeight;
    private float initialScale;
    private boolean dirty;
    private boolean controlsExpanded;
    private final HudLayoutHistory history=new HudLayoutHistory();
    private ClientUi.ActionButton undoButton;
    private int guideX=-1,guideY=-1;
    private final List<ClientUi.ActionButton> appearanceButtons = new ArrayList<>();
    /** Properties panel buttons with their offsets inside the panel; they follow the selection. */
    private final List<ClientUi.ActionButton> propertyButtons = new ArrayList<>();
    private final List<int[]> propertyOffsets = new ArrayList<>();
    static final int PROPERTIES_WIDTH = 136, PROPERTIES_HEIGHT = 118;
    private String message = "Drag / corner resize / Shift + arrows";
    public HudEditorScreen(Screen parent) { super(net.minecraft.client.Minecraft.getInstance(), UiFonts.body(), Component.literal("HUD Editor")); this.parent = parent; }
    HudEditorScreen(net.minecraft.client.Minecraft client, net.minecraft.client.gui.Font font, Screen parent) {
        super(client, font, Component.literal("HUD Editor")); this.parent = parent;
    }
    public record ToolbarLayout(int x, int y, int width, int height) {
        public boolean contains(double mx, double my) {
            return mx >= x && my >= y && mx < x + width && my < y + height;
        }
    }
    public static ToolbarLayout toolbarLayout(int screenWidth, int screenHeight, boolean expanded) {
        int w = Math.min(expanded ? 440 : 76, Math.max(1, screenWidth - 16));
        int h = Math.min(expanded ? 91 : 22, Math.max(1, screenHeight - 16));
        return new ToolbarLayout(expanded ? (screenWidth - w) / 2 : Math.max(0, screenWidth - w - 8), 8, w, h);
    }
    /**
     * Properties card for the selected overlay: right edge, or the left edge when the overlay sits on the right.
     * It stays above the vanilla hotbar/health/XP band and never covers the expanded toolbar; null when it cannot.
     */
    public static ToolbarLayout propertiesLayout(int screenWidth, int screenHeight, int elementX, int elementY,
                                                 int elementWidth, int elementHeight, boolean expanded) {
        int w = PROPERTIES_WIDTH, h = PROPERTIES_HEIGHT;
        if (screenWidth < w + 16 || screenHeight < h + 96) return null;
        int y = Math.min(Math.max(36, (screenHeight - h) / 2), screenHeight - 60 - h);
        var toolbar = toolbarLayout(screenWidth, screenHeight, expanded);
        for (int x : new int[] {screenWidth - w - 8, 8}) {
            // Keep a few pixels of air around the selection so its handle stays easy to grab.
            boolean coversElement = x - 6 < elementX + elementWidth && x + w + 6 > elementX && y - 6 < elementY + elementHeight && y + h + 6 > elementY;
            boolean coversToolbar = x < toolbar.x() + toolbar.width() && x + w > toolbar.x() && y < toolbar.y() + toolbar.height() + 6;
            if (!coversElement && !coversToolbar) return new ToolbarLayout(x, y, w, h);
        }
        return null;
    }
    private ToolbarLayout properties() {
        if (selected == null || !HudModules.enabled(selected)) return null;
        var b = HudModules.bounds(selected, width, height);
        return propertiesLayout(width, height, b.x(), b.y(), b.width(), b.height(), controlsExpanded);
    }
    private void property(int dx, int dy, int w, int h, String label, String tooltip, Runnable action) {
        var button = new ClientUi.ActionButton(0, 0, w, h, Component.literal(label), b -> action.run());
        button.setTooltip(Tooltip.create(Component.literal(tooltip)));
        propertyButtons.add(addRenderableWidget(button));
        propertyOffsets.add(new int[] {dx, dy});
    }
    private void centerSelected() {
        if (selected == null || !HudModules.enabled(selected)) return;
        var element = selected;
        history.change(() -> {
            var b = HudModules.bounds(element, width, height);
            HudModules.position(element, Math.max(0, (width - b.width()) / 2), b.y());
        });
        dirty = true;
    }
    private void layoutProperties() {
        var panel = properties();
        for (int i = 0; i < propertyButtons.size(); i++) {
            var button = propertyButtons.get(i);
            button.visible = button.active = panel != null;
            if (panel != null) button.setPosition(panel.x() + propertyOffsets.get(i)[0], panel.y() + propertyOffsets.get(i)[1]);
        }
    }
    private void toggleControls() {
        history.commit();dragging=resizing=null;guideX=guideY=-1;
        controlsExpanded = !controlsExpanded;
        setDragging(false);
        rebuildWidgets();
    }
    @Override protected void setInitialFocus() {
        // The explicit null-client constructor also supports isolated input/layout tests.
        if (minecraft != null) super.setInitialFocus();
    }
    @Override protected void init() {
        history.commit();guideX=guideY=-1;
        dragging = resizing = null;
        appearanceButtons.clear();
        undoButton=null;
        propertyButtons.clear(); propertyOffsets.clear();
        if (selected != null && !HudModules.enabled(selected)) selected = null;
        int inner = PROPERTIES_WIDTH - 16;
        property(inner - 32, 26, 18, 16, "-", "Smaller", () -> change(-.1f, 0));
        property(inner - 10, 26, 18, 16, "+", "Larger", () -> change(.1f, 0));
        property(inner - 32, 48, 18, 16, "-", "More transparent", () -> change(0, -.05f));
        property(inner - 10, 48, 18, 16, "+", "More opaque", () -> change(0, .05f));
        property(8, 70, (inner - 4) / 2, 18, "Center", "Center horizontally", this::centerSelected);
        property(12 + (inner - 4) / 2, 70, (inner - 4) / 2, 18, "Settings", "Open this module's settings", () -> {
            if (selected != null) minecraft.gui.setScreen(new VortexScreen(this, ClientModule.forHud(selected)));
        });
        layoutProperties();
        var toolbar = toolbarLayout(width, height, controlsExpanded);
        int w = toolbar.width(), x = toolbar.x(), y = toolbar.y();
        if (!controlsExpanded) {
            var controls = new ClientUi.ActionButton(x, y, w, toolbar.height(), Component.literal("Controls"), b -> toggleControls());
            controls.setTooltip(Tooltip.create(Component.literal("Show editor controls (H). Escape saves and closes.")));
            addRenderableWidget(controls);
            return;
        }
        var collapse = new ClientUi.ActionButton(x + w - 27, y + 4, 20, 16, Component.literal("-"), b -> toggleControls());
        collapse.setTooltip(Tooltip.create(Component.literal("Hide controls (H) to see the full HUD.")));
        addRenderableWidget(collapse);
        undoButton=addRenderableWidget(new ClientUi.ActionButton(x+w-79,y+4,46,16,Component.literal("Undo"),b->undo()));
        undoButton.setTooltip(Tooltip.create(Component.literal("Undo the last edit (Ctrl+Z). One drag is one step.")));
        int buttonWidth = (w - 34) / 4;
        String[] appearance = {"Size -", "Size +", "Opacity -", "Opacity +"};
        for (int i = 0; i < appearance.length; i++) {
            int index = i;
            var button = new ClientUi.ActionButton(x + 8 + i * (buttonWidth + 6), y + 24, buttonWidth, 20,
                Component.literal(appearance[i]), b -> {
                    if (index < 2) change(index == 0 ? -.1f : .1f, 0);
                    else change(0, index == 2 ? -.05f : .05f);
                });
            button.setTooltip(Tooltip.create(Component.literal("Select an overlay first. Each overlay has its own size and opacity.")));
            appearanceButtons.add(addRenderableWidget(button));
        }
        addRenderableWidget(new ClientUi.ActionButton(x + 8, y + 49, buttonWidth, 20,
            Component.literal(VortexClient.config.hudEditorGrid ? "Grid: ON" : "Grid: OFF"), b -> {
                VortexClient.config.hudEditorGrid = !VortexClient.config.hudEditorGrid;
                b.setMessage(Component.literal(VortexClient.config.hudEditorGrid ? "Grid: ON" : "Grid: OFF")); dirty = true;
            }));
        addRenderableWidget(new ClientUi.ActionButton(x + 14 + buttonWidth, y + 49, buttonWidth, 20,
            Component.literal(VortexClient.config.hudEditorSnapping ? "Snap: ON" : "Snap: OFF"), b -> {
                VortexClient.config.hudEditorSnapping = !VortexClient.config.hudEditorSnapping;
                b.setMessage(Component.literal(VortexClient.config.hudEditorSnapping ? "Snap: ON" : "Snap: OFF")); dirty = true;
            }));
        addRenderableWidget(new ClientUi.ActionButton(x + 20 + buttonWidth * 2, y + 49, buttonWidth, 20,
            Component.literal("Reset"), b -> { history.change(HudModules::resetPositions); dirty = true; }));
        addRenderableWidget(new ClientUi.ActionButton(x + 26 + buttonWidth * 3, y + 49, buttonWidth, 20,
            Component.literal("Done"), b -> onClose(), true));
    }
    @Override public boolean isInGameUi() { return minecraft != null && minecraft.level != null; }
    @Override public void extractBackground(GuiGraphicsExtractor g, int mouseX, int mouseY, float delta) {
        if (!isInGameUi()) {
            // Keep the normal panorama when the editor is opened outside a world.
            super.extractBackground(g, mouseX, mouseY, delta);
            return;
        }
        // Minecraft already rendered its real HUD. Do not blur, shade or draw it a second time.
        minecraft.gui.hud.extractDeferredSubtitles();
    }
    @Override public void extractRenderState(GuiGraphicsExtractor g, int mouseX, int mouseY, float delta) {
        if (VortexClient.config.hudEditorGrid) {
            for (int x = 0; x < width; x += 24) g.verticalLine(x, 0, height, 0x18A0A4B3);
            for (int y = 0; y < height; y += 24) g.horizontalLine(0, width, y, 0x18A0A4B3);
        }
        if (dragging != null && VortexClient.config.hudEditorSnapping) {
            // Snap guides are dashed lavender lines across the whole screen.
            if(guideX>=0)ClientUi.dashed(g,Math.min(width-1,guideX),0,1,height,ClientUi.accentAlpha(0xCC));
            if(guideY>=0)ClientUi.dashed(g,0,Math.min(height-1,guideY),width,1,ClientUi.accentAlpha(0xCC));
        }
        VortexClient.drawFps(g, true); HudModules.draw(g, true);
        boolean any = false;
        for (var element : HudModules.Element.values()) {
            if (!HudModules.enabled(element)) continue;
            any = true;
            var b = HudModules.bounds(element, width, height);
            boolean hovered = b.contains(mouseX, mouseY);
            if (selected != element)
                ClientUi.dashed(g, b.x() - 2, b.y() - 2, b.width() + 4, b.height() + 4, hovered ? ClientUi.ACCENT : 0x99ECEDF3);
            if (selected == element) {
                if (resizing == element || HudModules.resizeHandleContains(b, mouseX, mouseY))
                    g.requestCursor(com.mojang.blaze3d.platform.cursor.CursorTypes.CROSSHAIR);
                else if (dragging == element || b.contains(mouseX, mouseY))
                    g.requestCursor(com.mojang.blaze3d.platform.cursor.CursorTypes.RESIZE_ALL);
                g.outline(b.x() - 2, b.y() - 2, b.width() + 4, b.height() + 4, ClientUi.ACCENT);
                g.outline(b.x() - 3, b.y() - 3, b.width() + 6, b.height() + 6, ClientUi.accentAlpha(0x66));
                // Name and size ride above the selection so they never cover the overlay itself.
                String tag = HudModules.name(element) + " · " + Math.round(HudModules.scale(element) * 100) + "%";
                int tagWidth = UiFonts.body().width(tag) + 12, tagY = b.y() - 17 >= 0 ? b.y() - 17 : b.y() + b.height() + 5;
                ClientUi.pill(g, b.x() - 2, tagY, tagWidth, 12, ClientUi.ACCENT);
                g.text(UiFonts.body(), tag, b.x() + 4, tagY + 2, ClientUi.ACCENT_TEXT, false);
                ClientUi.rounded(g, b.x() + b.width() - 4, b.y() + b.height() - 4, 9, 9, 4, ClientUi.ACCENT);
                ClientUi.rounded(g, b.x() + b.width() - 2, b.y() + b.height() - 2, 5, 5, 2, 0xFFFFFFFF);
            }
        }
        if (!any) {
            g.centeredText(font, "No HUD modules enabled", width / 2, height / 2 - 24, ClientUi.TEXT);
            g.centeredText(font, "Enable a module in the Vortex menu.", width / 2, height / 2 - 7, ClientUi.MUTED);
        }
        if (controlsExpanded) {
            var toolbar = toolbarLayout(width, height, true);
            int w = toolbar.width(), x = toolbar.x(), y = toolbar.y();
            ClientUi.rounded(g, x - 1, y + 3, w + 2, toolbar.height(), 12, 0x40000000);
            ClientUi.rounded(g, x, y, w, toolbar.height(), 12, ClientUi.GLASS);
            String selection = selected == null ? "HUD Editor  /  Select an overlay" :
                String.format(Locale.ROOT, "%s   %d%% size / %d%% opacity", HudModules.name(selected),
                    Math.round(HudModules.scale(selected) * 100), Math.round(HudModules.opacity(selected) * 100));
            ClientUi.text(g, selection, x + 8, y + 9, w - 92, ClientUi.TEXT);
            ClientUi.text(g, message, x + 8, y + 77, w - 16, ClientUi.MUTED);
        }
        layoutProperties();
        var panel = properties();
        if (panel != null) {
            int px = panel.x(), py = panel.y();
            ClientUi.rounded(g, px - 1, py + 3, panel.width() + 2, panel.height(), 12, 0x40000000);
            ClientUi.rounded(g, px, py, panel.width(), panel.height(), 12, ClientUi.GLASS);
            ClientUi.eyebrow(g, "Selected", px + 8, py + 6, panel.width() - 16);
            ClientUi.text(g, HudModules.name(selected), px + 8, py + 15, panel.width() - 16, ClientUi.TEXT);
            ClientUi.text(g, "Size " + Math.round(HudModules.scale(selected) * 100) + "%", px + 8, py + 30, panel.width() - 60, ClientUi.MUTED);
            ClientUi.text(g, "Opacity " + Math.round(HudModules.opacity(selected) * 100) + "%", px + 8, py + 52, panel.width() - 60, ClientUi.MUTED);
            var b = HudModules.bounds(selected, width, height);
            ClientUi.text(g, "X " + b.x() + "  Y " + b.y(), px + 8, py + 96, panel.width() - 16, ClientUi.MUTED);
        }
        if (!isInGameUi())
            g.centeredText(font, "Open a world to preview Minecraft's HUD.", width / 2, height - 14, ClientUi.MUTED);
        else if (minecraft.gui.hud.isHidden())
            g.centeredText(font, "HUD hidden by F1. Close the editor and press F1.", width / 2, height - 14, ClientUi.MUTED);
        for (var button : appearanceButtons) button.active = selected != null && HudModules.enabled(selected);
        if(undoButton!=null)undoButton.active=history.canUndo() || dragging!=null || resizing!=null;
        super.extractRenderState(g, mouseX, mouseY, delta);
    }
    @Override public boolean mouseClicked(MouseButtonEvent event, boolean doubleClick) {
        if (super.mouseClicked(event, doubleClick)) return true;
        if (controlsExpanded && toolbarLayout(width, height, true).contains(event.x(), event.y())) return true;
        var panel = properties();
        // A click on the card's background keeps the selection instead of clearing it.
        if (panel != null && panel.contains(event.x(), event.y())) return true;
        if (HudModules.isDragButton(event.button()) && selected != null && HudModules.enabled(selected)) {
            var b = HudModules.bounds(selected, width, height);
            if (HudModules.resizeHandleContains(b, event.x(), event.y())) {
                resizing = selected; dragging = null;
                history.begin();
                initialScale = HudModules.scale(selected); initialWidth = b.width(); initialHeight = b.height();
                resizeX = event.x(); resizeY = event.y(); setDragging(true); return true;
            }
        }
        if (!HudModules.isDragButton(event.button()) && event.button() != InputConstants.MOUSE_BUTTON_RIGHT) return false;
        var elements = HudModules.Element.values();
        for (int i = elements.length - 1; i >= 0; i--) {
            var element = elements[i];
            if (!HudModules.enabled(element)) continue;
            var b = HudModules.bounds(element, width, height);
            if (b.contains(event.x(), event.y())) {
                selected = element;
                if (event.button() == InputConstants.MOUSE_BUTTON_RIGHT) {
                    minecraft.gui.setScreen(new VortexScreen(this, ClientModule.forHud(element)));
                } else {
                    dragging = element; resizing = null; setDragging(true);
                    history.begin();
                    offsetX = event.x() - b.x(); offsetY = event.y() - b.y();
                }
                return true;
            }
        }
        selected = null;
        return false;
    }
    @Override public boolean mouseDragged(MouseButtonEvent event, double dx, double dy) {
        if (!HudModules.isDragButton(event.button())) return super.mouseDragged(event, dx, dy);
        if (resizing != null) {
            HudModules.appearance(resizing, HudModules.resizeScale(initialScale, initialWidth, initialHeight,
                event.x() - resizeX, event.y() - resizeY), HudModules.opacity(resizing));
            keepOnScreen(resizing); dirty = true; return true;
        }
        if (dragging == null) return super.mouseDragged(event, dx, dy);
        var b = HudModules.bounds(dragging, width, height);
        int x = (int)(event.x() - offsetX), y = (int)(event.y() - offsetY);
        if (VortexClient.config.hudEditorSnapping) {
            var others=new ArrayList<HudModules.Bounds>();
            for(var element:HudModules.Element.values())if(element!=dragging && HudModules.enabled(element))others.add(HudModules.bounds(element,width,height));
            int[] xs=new int[others.size()*3],ys=new int[others.size()*3];
            for(int i=0;i<others.size();i++){var other=others.get(i);xs[i*3]=other.x();xs[i*3+1]=other.x()+other.width()/2;xs[i*3+2]=other.x()+other.width();
                ys[i*3]=other.y();ys[i*3+1]=other.y()+other.height()/2;ys[i*3+2]=other.y()+other.height();}
            var sx=HudSnap.axis(x,b.width(),width,xs);var sy=HudSnap.axis(y,b.height(),height,ys);
            x=sx.position();y=sy.position();guideX=sx.guide();guideY=sy.guide();
        } else {
            guideX=guideY=-1;
            x = Math.clamp(x, 0, Math.max(0, width - b.width())); y = Math.clamp(y, 0, Math.max(0, height - b.height()));
        }
        HudModules.position(dragging, x, y);
        dirty = true; return true;
    }
    @Override public boolean mouseReleased(MouseButtonEvent event) {
        if ((dragging != null || resizing != null) && HudModules.isDragButton(event.button())) {
            history.commit();guideX=guideY=-1;
            dragging = resizing = null; setDragging(false); return true;
        }
        return super.mouseReleased(event);
    }
    @Override public boolean mouseScrolled(double x, double y, double horizontal, double vertical) {
        if (selected == null || !Double.isFinite(vertical) || vertical == 0) return super.mouseScrolled(x, y, horizontal, vertical);
        change((float)Math.clamp(vertical * .05, -.25, .25), 0);
        return true;
    }
    @Override public boolean keyPressed(KeyEvent event) {
        if(event.key()==InputConstants.KEY_Z && event.hasControlDown()){undo();return true;}
        if (event.key() == InputConstants.KEY_H) { toggleControls(); return true; }
        if (selected != null && HudModules.enabled(selected)) {
            int step = event.hasShiftDown() ? 10 : 1;
            int dx = switch (event.key()) { case InputConstants.KEY_LEFT -> -step; case InputConstants.KEY_RIGHT -> step; default -> 0; };
            int dy = switch (event.key()) { case InputConstants.KEY_UP -> -step; case InputConstants.KEY_DOWN -> step; default -> 0; };
            if (dx != 0 || dy != 0) {
                history.begin();
                var b = HudModules.bounds(selected, width, height);
                HudModules.position(selected, Math.clamp(b.x() + dx, 0, Math.max(0, width - b.width())),
                    Math.clamp(b.y() + dy, 0, Math.max(0, height - b.height())));
                history.commit();dirty = true; return true;
            }
        }
        return super.keyPressed(event);
    }
    private void keepOnScreen(HudModules.Element element) {
        HudModules.keepOnScreen(element, width, height);
    }
    private void change(float scale, float opacity) {
        if (selected == null || !HudModules.enabled(selected)) return;
        history.begin();
        HudModules.appearance(selected, HudModules.scale(selected) + scale, HudModules.opacity(selected) + opacity);
        keepOnScreen(selected);history.commit();dirty = true;
    }
    private void undo(){
        if(history.undo()){dirty=true;message="Last HUD edit undone. Ctrl+Z / drag / arrows";}
        else message="Nothing to undo.";
        dragging=resizing=null;guideX=guideY=-1;setDragging(false);
    }
    private boolean save() {
        history.commit();
        if (!dirty) return true;
        if (!VortexClient.config.save()) { message = "Could not save layout. See game log.";ClientNotifications.error("hud-save-error","HUD not saved","Could not save the layout. Please try again."); return false; }
        dirty = false;ClientNotifications.success("hud-saved","HUD saved","Your layout and overlay settings have been saved."); return true;
    }
    @Override public void onClose() {
        if (save()) minecraft.gui.setScreen(parent);
        else if (!controlsExpanded) { controlsExpanded = true; rebuildWidgets(); }
    }
    @Override public void removed() { save(); }
    @Override public boolean isPauseScreen() { return false; }
}
