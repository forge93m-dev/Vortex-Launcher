package dev.vortex.client;

import java.util.function.DoubleConsumer;
import java.util.function.DoubleFunction;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSliderButton;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.InputWithModifiers;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;

/** Lightweight UI with bundled antialiased icons. No browser or runtime native dependencies. */
final class ClientUi {
    static final int PANEL = 0xFF101114, SURFACE = 0xFF191B20, BORDER = 0xFF2D3038;
    static final int TEXT = 0xFFE9EBF0, MUTED = 0xFF979DA9, ACCENT = 0xFFA7B4CA;
    private static final java.util.Map<String, net.minecraft.resources.Identifier> ICONS = new java.util.HashMap<>();
    private ClientUi() { }

    static void rounded(GuiGraphicsExtractor g, int x, int y, int w, int h, int radius, int color) {
        RoundedShapes.draw(g, x, y, w, h, radius, color);
    }

    static void panel(GuiGraphicsExtractor g, int x, int y, int w, int h, int radius, int border, int background) {
        rounded(g, x, y, w, h, radius, border);
        rounded(g, x + 1, y + 1, w - 2, h - 2, Math.max(0, radius - 1), background);
    }

    static int mix(int first, int second, float amount) {
        float t = Math.clamp(amount, 0, 1);
        int result = 0;
        for (int shift = 0; shift <= 24; shift += 8)
            result |= Math.round(((first >>> shift) & 255) * (1 - t) + ((second >>> shift) & 255) * t) << shift;
        return result;
    }

    static float approach(float current, float target, double seconds) {
        if(!VortexClient.config.uiAnimations)return target;
        if(!Double.isFinite(seconds))return current;
        return current + (target - current) * (float)(1 - Math.exp(-Math.clamp(seconds, 0, .1) * 18));
    }

    static void toggle(GuiGraphicsExtractor g, int x, int y, float state) {
        rounded(g, x, y, 26, 14, 7, mix(0xFF41434F, ACCENT, state));
        rounded(g, x + 2 + Math.round(state * 12), y + 2, 10, 10, 5, mix(TEXT, 0xFF24202A, state));
    }

    static void text(GuiGraphicsExtractor g, String text, int x, int y, int maxWidth, int color) {
        var font = UiFonts.body();
        g.text(font, font.plainSubstrByWidth(text, Math.max(0, maxWidth)), x, y, color, false);
    }

    static void heading(GuiGraphicsExtractor g, String text, int x, int y, int maxWidth) {
        g.pose().pushMatrix();
        g.pose().translate(x, y); g.pose().scale(1.25f, 1.25f);
        var font = UiFonts.heading();
        g.text(font, font.plainSubstrByWidth(text, (int)(maxWidth / 1.25f)), 0, 0, TEXT, false);
        g.pose().popMatrix();
    }

    /** Opt-in chrome for client tools only; never applied to inventory, HUD or wardrobe. */
    static void beginPage(GuiGraphicsExtractor g, int screenWidth, int screenHeight, int x, int y, int w, int h,
                          String title, String icon, long opened) {
        float progress = UiMotion.progress(opened);
        g.fill(0, 0, screenWidth, screenHeight, ((int)(150 * progress) << 24) | 0x080C10);
        UiMotion.begin(g, x, y, w, h, 1, (1 - progress) * 6);
        rounded(g, x - 2, y + 4, w + 4, h, 12, 0x44000000);
        panel(g, x, y, w, h, 10, BORDER, 0xFF11171B);
        rounded(g, x + 1, y + 1, w - 2, 32, 9, 0xFF1A2327);
        g.fill(x + 1, y + 25, x + w - 1, y + 34, 0xFF1A2327);
        icon(g, icon, x + 12, y + 10, 16, 0xFFD2E8C9);
        heading(g, title, x + 36, y + 12, w - 135);
        g.fill(x + 12, y + 34, x + w - 12, y + 35, BORDER);
    }

    static void moduleIcon(GuiGraphicsExtractor g, ClientModule module, int x, int y, int color, int background) {
        icon(g, module.name().toLowerCase(java.util.Locale.ROOT), x + 2, y + 2, 21, color);
    }
    static void icon(GuiGraphicsExtractor g, String name, int x, int y, int size, int color) {
        var texture = ICONS.computeIfAbsent(name, key -> net.minecraft.resources.Identifier.fromNamespaceAndPath("vortex_client", "textures/ui/icons/" + key + ".png"));
        int resolution = name.equals("brand") ? 1254 : 96;
        if (name.equals("brand")) color = (color & 0xFF000000) | 0xFFFFFF;
        g.blit(net.minecraft.client.renderer.RenderPipelines.GUI_TEXTURED, texture, x, y, 0, 0, size, size, resolution, resolution, resolution, resolution, color);
    }

    static class ActionButton extends Button {
        private float hover;
        private long previousFrame = System.nanoTime();
        private final boolean primary;
        private long pressed;
        protected void markPress() { pressed = System.nanoTime(); }
        protected float pressScale(long now) {
            double elapsed = (now - pressed) / 1e6;
            return VortexClient.config.uiAnimations && elapsed < 160 ? 1 - .025f * (float)Math.sin(Math.PI * elapsed / 160) : 1;
        }
        @Override public void onPress(InputWithModifiers input) { markPress(); super.onPress(input); }
        ActionButton(int x, int y, int w, int h, Component label, OnPress press) {
            this(x, y, w, h, label, press, false);
        }
        ActionButton(int x, int y, int w, int h, Component label, OnPress press, boolean primary) {
            super(x, y, w, h, label, press, DEFAULT_NARRATION);
            this.primary = primary;
        }
        @Override protected void extractContents(GuiGraphicsExtractor g, int mx, int my, float delta) {
            long now = System.nanoTime();
            hover = approach(hover, active && isHoveredOrFocused() ? 1 : 0, (now - previousFrame) / 1e9);
            previousFrame = now;
            UiMotion.begin(g, getX(), getY(), getWidth(), getHeight(), pressScale(now), 0);
            int background = primary ? mix(0xFF293344, 0xFF35435A, hover) : mix(SURFACE, 0xFF262A33, hover);
            panel(g, getX(), getY(), getWidth(), getHeight(), 4,
                active ? mix(BORDER, ACCENT, hover * .55f) : BORDER, active ? background : 0xFF1A1B21);
            if(hover>.01f)rounded(g,getX()+6,getY()+getHeight()-2,Math.max(1,Math.round((getWidth()-12)*hover)),1,0,
                ((int)(hover*110)<<24)|(ACCENT&0xFFFFFF));
            var font = UiFonts.body();
            String label = font.plainSubstrByWidth(getMessage().getString(), getWidth() - 8);
            g.text(font, label, getX() + (getWidth() - font.width(label)) / 2, getY() + (getHeight() - 9) / 2,
                !active ? 0xFF686A78 : TEXT, false);
            g.pose().popMatrix();
        }
    }

    static final class Slider extends AbstractSliderButton {
        private float hover;
        private long frame=System.nanoTime();
        private final String label;
        private final double minimum, maximum;
        private final DoubleConsumer change;
        private final DoubleFunction<String> format;
        private final Runnable commit;
        Slider(int x, int y, int w, String label, double minimum, double maximum, double initial,
               DoubleFunction<String> format, DoubleConsumer change, Runnable commit) {
            super(x, y, w, 37, Component.literal(label), Math.clamp((initial - minimum) / (maximum - minimum), 0, 1));
            this.label = label; this.minimum = minimum; this.maximum = maximum;
            this.format = format; this.change = change; this.commit = commit;
            updateMessage();
        }
        private double current() { return minimum + value * (maximum - minimum); }
        @Override protected void updateMessage() { setMessage(Component.literal(label + ": " + format.apply(current()))); }
        @Override protected void applyValue() { change.accept(current()); }
        @Override public void onRelease(MouseButtonEvent event) { super.onRelease(event); commit.run(); }
        @Override public boolean keyPressed(KeyEvent event) {
            boolean handled = super.keyPressed(event);
            if (handled) commit.run();
            return handled;
        }
        @Override public void extractWidgetRenderState(GuiGraphicsExtractor g, int mx, int my, float delta) {
            long now=System.nanoTime();hover=approach(hover,isHoveredOrFocused()?1:0,(now-frame)/1e9);frame=now;
            var font = UiFonts.body();
            String formatted = format.apply(current());
            text(g, label, getX(), getY() + 2, getWidth() - font.width(formatted) - 10, TEXT);
            g.text(font, formatted, getX() + getWidth() - font.width(formatted), getY() + 2, ACCENT, false);
            rounded(g, getX() + 4, getY() + 24, getWidth() - 8, 4, 2, BORDER);
            int position = (int)Math.round(value * (getWidth() - 12));
            rounded(g, getX() + 4, getY() + 24, position + 2, 4, 2, ACCENT);
            rounded(g, getX() + position, getY() + 20, 12, 12, 6, mix(ACCENT,TEXT,hover));
            handleCursor(g);
        }
    }
}
