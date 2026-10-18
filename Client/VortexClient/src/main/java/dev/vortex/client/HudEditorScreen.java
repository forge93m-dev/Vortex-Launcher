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
        if (selected != null && !HudModules.enabled(selected)) selected = null;
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
            for (int x = 0; x < width; x += 24) g.verticalLine(x, 0, height, 0x185F6578);
            for (int y = 0; y < height; y += 24) g.horizontalLine(0, width, y, 0x185F6578);
        }
        if (dragging != null && VortexClient.config.hudEditorSnapping) {
            if(guideX>=0)g.verticalLine(Math.min(width-1,guideX),0,height,0xCC9BB9DF);
            if(guideY>=0)g.horizontalLine(0,width,Math.min(height-1,guideY),0xCC9BB9DF);
        }
        VortexClient.drawFps(g, true); HudModules.draw(g, true);
        boolean any = false;
        for (var element : HudModules.Element.values()) {
            if (!HudModules.enabled(element)) continue;
            any = true;
            var b = HudModules.bounds(element, width, height);
            boolean highlighted = selected == element || b.contains(mouseX, mouseY);
            g.outline(b.x() - 1, b.y() - 1, b.width() + 2, b.height() + 2,
                highlighted ? ClientUi.ACCENT : 0x885F6578);
            if (selected == element) {
                if (resizing == element || HudModules.resizeHandleContains(b, mouseX, mouseY))
                    g.requestCursor(com.mojang.blaze3d.platform.cursor.CursorTypes.CROSSHAIR);
                else if (dragging == element || b.contains(mouseX, mouseY))
                    g.requestCursor(com.mojang.blaze3d.platform.cursor.CursorTypes.RESIZE_ALL);
                for (int cornerX : new int[] {b.x() - 2, b.x() + b.width() - 2})
                    for (int cornerY : new int[] {b.y() - 2, b.y() + b.height() - 2})
                        g.fill(cornerX, cornerY, cornerX + 4, cornerY + 4, ClientUi.ACCENT);
                ClientUi.rounded(g, b.x() + b.width() - 5, b.y() + b.height() - 5, 10, 10, 3, ClientUi.ACCENT);
                g.fill(b.x() + b.width() - 2, b.y() + b.height() - 1, b.x() + b.width() + 2, b.y() + b.height(), ClientUi.PANEL);
                g.fill(b.x() + b.width(), b.y() + b.height() - 3, b.x() + b.width() + 1, b.y() + b.height(), ClientUi.PANEL);
            }
        }
        if (!any) {
            g.centeredText(font, "No HUD modules enabled", width / 2, height / 2 - 24, ClientUi.TEXT);
            g.centeredText(font, "Enable a module in the Vortex menu.", width / 2, height / 2 - 7, ClientUi.MUTED);
        }
        if (controlsExpanded) {
            var toolbar = toolbarLayout(width, height, true);
            int w = toolbar.width(), x = toolbar.x(), y = toolbar.y();
            ClientUi.panel(g, x, y, w, toolbar.height(), 8, ClientUi.BORDER, ClientUi.PANEL);
            String selection = selected == null ? "HUD Editor  /  Select an overlay" :
                String.format(Locale.ROOT, "%s   %d%% size / %d%% opacity", HudModules.name(selected),
                    Math.round(HudModules.scale(selected) * 100), Math.round(HudModules.opacity(selected) * 100));
            ClientUi.text(g, selection, x + 8, y + 9, w - 92, ClientUi.TEXT);
            ClientUi.text(g, message, x + 8, y + 77, w - 16, ClientUi.MUTED);
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
