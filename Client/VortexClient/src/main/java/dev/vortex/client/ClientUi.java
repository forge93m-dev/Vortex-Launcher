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
    // Soft theme shared with the launcher's Midnight look: one accent colour (lavender by default), dark text on accent fills.
    static final int PANEL = 0xFF15161C, SURFACE = 0xFF1F2128, BORDER = 0xFF2E3140;
    static final int TEXT = 0xFFECEDF3, MUTED = 0xFFA0A4B3, RAISED = 0xFF2B2E3A, TRACK = 0xFF3A3D4A;
    /** Accent shades, all derived from the chosen colour by {@link #applyAccent}. */
    static int ACCENT, ACCENT_TEXT, ACCENT_SOFT, ACCENT_LIGHT, ACCENT_PALE, ACCENT_TINT, ACCENT_TINT_STRONG, ACCENT_DEEP, ACCENT_DEEP_HOVER, ACCENT_FAINT;
    /** Preset accent colours offered in Client settings. */
    static final int[] ACCENTS = {0xA78BFA, 0x7DD3FC, 0x6EE7B7, 0xBEF264, 0xFDE68A, 0xFDBA74, 0xFB7185, 0xF9A8D4};
    static final String[] ACCENT_NAMES = {"Lavender", "Sky", "Mint", "Lime", "Gold", "Peach", "Rose", "Pink"};
    static { applyAccent(0xA78BFA); }
    /**
     * Sets the accent and its shades. The mixing amounts reproduce the original lavender palette exactly,
     * so other colours get the same light, pale and dark-tinted variants.
     */
    static void applyAccent(int rgb) {
        int accent = 0xFF000000 | rgb & 0xFFFFFF;
        ACCENT = accent;
        ACCENT_SOFT = 0x33000000 | rgb & 0xFFFFFF;
        ACCENT_LIGHT = mix(accent, 0xFFFFFFFF, .34f);
        ACCENT_PALE = mix(accent, 0xFFFFFFFF, .63f);
        ACCENT_TINT = mix(PANEL, accent, .2f);
        ACCENT_TINT_STRONG = mix(PANEL, accent, .33f);
        ACCENT_DEEP = mix(PANEL, accent, .145f);
        ACCENT_DEEP_HOVER = mix(PANEL, accent, .27f);
        ACCENT_FAINT = mix(PANEL, accent, .1f);
        // Dark text on light accents, light text on dark ones.
        int luminance = ((rgb >> 16 & 255) * 299 + (rgb >> 8 & 255) * 587 + (rgb & 255) * 114) / 1000;
        ACCENT_TEXT = luminance > 110 ? mix(accent, 0xFF000000, .82f) : 0xFFF3F4F7;
    }
    /** The accent with another alpha (0-255), for translucent fills and guides. */
    static int accentAlpha(int alpha) { return alpha << 24 | ACCENT & 0xFFFFFF; }
    static final int GLASS = 0xF015161C, DANGER = 0xFFFDA4AF, DANGER_FILL = 0x33FB7185;
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
        rounded(g, x, y, 26, 14, 7, mix(TRACK, ACCENT, state));
        rounded(g, x + 2 + Math.round(state * 12), y + 2, 10, 10, 5, 0xFFFFFFFF);
    }

    /** Fully rounded ends: the soft theme's buttons, chips and tracks. */
    static void pill(GuiGraphicsExtractor g, int x, int y, int w, int h, int color) {
        rounded(g, x, y, w, h, h / 2, color);
    }

    /** A one-pixel dashed rectangle, used for editable outlines that must not hide what they frame. */
    static void dashed(GuiGraphicsExtractor g, int x, int y, int w, int h, int color) {
        for (int i = 0; i < w; i += 6) {
            g.fill(x + i, y, x + Math.min(w, i + 3), y + 1, color);
            g.fill(x + i, y + h - 1, x + Math.min(w, i + 3), y + h, color);
        }
        for (int i = 0; i < h; i += 6) {
            g.fill(x, y + i, x + 1, y + Math.min(h, i + 3), color);
            g.fill(x + w - 1, y + i, x + w, y + Math.min(h, i + 3), color);
        }
    }

    /** Uppercase section label used above groups of controls. */
    static void eyebrow(GuiGraphicsExtractor g, String text, int x, int y, int maxWidth) {
        text(g, text.toUpperCase(java.util.Locale.ROOT), x, y, maxWidth, MUTED);
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
        g.fill(0, 0, screenWidth, screenHeight, ((int)(165 * progress) << 24) | 0x0C0D12);
        // The window grows slightly into place while it fades up.
        UiMotion.begin(g, x, y, w, h, .96f + .04f * progress, (1 - progress) * 4);
        rounded(g, x - 2, y + 5, w + 4, h, 12, 0x40000000);
        rounded(g, x, y, w, h, 12, GLASS);
        rounded(g, x + 9, y + 7, 22, 22, 11, ACCENT_SOFT);
        icon(g, icon, x + 12, y + 10, 16, ACCENT);
        heading(g, title, x + 38, y + 12, w - 135);
        g.fill(x + 14, y + 34, x + w - 14, y + 35, 0x14FFFFFF);
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

    private static final net.minecraft.resources.Identifier PIN = net.minecraft.resources.Identifier.fromNamespaceAndPath("vortex_client", "textures/ui/pin.png");
    /** Smooth teardrop (17x26 GUI pixels, 8x texture with filtering) tinted with {@code color}. */
    static void pin(GuiGraphicsExtractor g, int x, int y, int color) {
        g.blit(net.minecraft.client.renderer.RenderPipelines.GUI_TEXTURED, PIN, x, y, 0, 0, 17, 26, 136, 208, 136, 208, color);
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
            // Pills lift half a pixel on hover; the label never scales.
            UiMotion.begin(g, getX(), getY(), getWidth(), getHeight(), pressScale(now), VortexClient.config.uiAnimations ? -hover * .5f : 0);
            int background = !active ? 0xFF1E2028 : primary ? mix(ACCENT, ACCENT_LIGHT, hover) : mix(RAISED, 0xFF383C4B, hover);
            if (isFocused() && !isHovered()) {
                // Keyboard focus is a ring that follows the pill's rounded ends, not a straight underline.
                pill(g, getX(), getY(), getWidth(), getHeight(), primary ? ACCENT_PALE : ACCENT);
                pill(g, getX() + 1, getY() + 1, getWidth() - 2, getHeight() - 2, background);
            } else pill(g, getX(), getY(), getWidth(), getHeight(), background);
            var font = UiFonts.body();
            String label = font.plainSubstrByWidth(getMessage().getString(), getWidth() - 12);
            g.text(font, label, getX() + (getWidth() - font.width(label)) / 2, getY() + (getHeight() - 9) / 2,
                !active ? 0xFF6B6E7B : primary ? ACCENT_TEXT : TEXT, false);
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
            rounded(g, getX() + 4, getY() + 24, getWidth() - 8, 4, 2, TRACK);
            int position = (int)Math.round(value * (getWidth() - 12));
            rounded(g, getX() + 4, getY() + 24, position + 2, 4, 2, ACCENT);
            if (hover > .01f) rounded(g, getX() + position - 3, getY() + 17, 18, 18, 9, ((int)(hover * 0x55) << 24) | (ACCENT & 0xFFFFFF));
            rounded(g, getX() + position, getY() + 20, 12, 12, 6, 0xFFFFFFFF);
            handleCursor(g);
        }
    }
}
