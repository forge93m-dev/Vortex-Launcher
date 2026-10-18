package dev.vortex.client;

import dev.vortex.client.mixin.FontAccessor;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GlyphSource;
import net.minecraft.client.gui.font.glyphs.EffectGlyph;
import net.minecraft.network.chat.FontDescription;
import net.minecraft.resources.Identifier;

/** Local UI font facades: correct text width/caret placement, live resource reloads. */
public final class UiFonts {
    private static Font original, body, heading;
    private static volatile long generation;
    private static final FontDescription.Resource MISSING = new FontDescription.Resource(net.minecraft.client.gui.font.FontManager.MISSING_FONT);
    private UiFonts() { }
    public static Font body() { refresh(); return body; }
    public static Font heading() { refresh(); return heading; }
    public static void invalidate() { generation++; }
    private static void refresh() {
        var mc = Minecraft.getInstance();
        if (mc == null || mc.font == null) return;
        if (original != mc.font) {
            original = mc.font;
            var provider = ((FontAccessor)(Object)original).vortex$provider();
            body = facade(provider, "ui"); heading = facade(provider, "heading");
        }
    }
    private static Font facade(Font.Provider provider, String name) {
        var resource = new FontDescription.Resource(Identifier.fromNamespaceAndPath("vortex_client", name));
        return new Font(new CachedProvider(provider, resource));
    }
    public static final class CachedProvider implements Font.Provider {
        private final Font.Provider provider;
        private final FontDescription.Resource resource;
        private GlyphSource source;
        private long seenGeneration = -1;
        public CachedProvider(Font.Provider provider, FontDescription.Resource resource) {
            this.provider = provider; this.resource = resource;
        }
        public GlyphSource glyphs(FontDescription ignored) {
            long current = generation;
            if (seenGeneration != current) {
                source = resolve(provider, resource);
                seenGeneration = current;
            }
            return source;
        }
        public EffectGlyph effect() { return provider.effect(); }
    }
    public static GlyphSource resolve(Font.Provider provider, FontDescription.Resource resource) {
        var source = provider.glyphs(resource);
        var missing = provider.glyphs(MISSING);
        // A failed/missing pack must leave the UI readable. Re-evaluate after resource reloads.
        return source == missing ? provider.glyphs(FontDescription.DEFAULT) : source;
    }
}
