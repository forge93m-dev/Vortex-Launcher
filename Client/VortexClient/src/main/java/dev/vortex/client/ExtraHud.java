package dev.vortex.client;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Locale;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.Hud;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.core.Holder;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.world.effect.MobEffectInstance;
import net.minecraft.world.effect.MobEffects;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.Items;

public final class ExtraHud {
    private static ItemStack sampleTool;
    public static String duration(int ticks, boolean infinite) {
        if (infinite) return "∞";
        long seconds = (Math.max(0L, ticks) + 19) / 20;
        return String.format(Locale.ROOT, "%d:%02d", seconds / 60, seconds % 60);
    }
    public static String durability(int maximum, int damage) {
        return maximum <= 0 ? "" : HudModules.armorDurability(maximum, damage) + " / " + maximum;
    }
    public static List<ItemStack> heldItems(Minecraft mc, boolean preview) {
        var items = new ArrayList<ItemStack>();
        if (mc != null && mc.player != null) {
            var main = mc.player.getMainHandItem();
            var off = mc.player.getOffhandItem();
            if (!main.isEmpty() && main.isDamageableItem()) items.add(main);
            if (VortexClient.config.heldOffhand && !off.isEmpty() && off.isDamageableItem()) items.add(off);
        }
        if (preview && items.isEmpty()) {
            if (sampleTool == null) {
                Item tool = Items.DIAMOND_PICKAXE;
                if (BuiltInRegistries.ITEM.wrapAsHolder(tool) instanceof Holder.Reference<Item> reference && !reference.areComponentsBound()) return items;
                sampleTool = new ItemStack(tool); sampleTool.setDamageValue(280);
            }
            items.add(sampleTool);
        }
        return items;
    }
    public static List<MobEffectInstance> effects(Minecraft mc, boolean preview) {
        if (mc != null && mc.player != null) {
            var active = mc.player.getActiveEffects().stream().filter(MobEffectInstance::showIcon)
                .sorted(Comparator.comparing(e -> e.getEffect().value().getDescriptionId())).toList();
            if (!active.isEmpty() || !preview) return active;
        }
        return preview ? List.of(new MobEffectInstance(MobEffects.SPEED, 2400, 1),
            new MobEffectInstance(MobEffects.STRENGTH, 1200), new MobEffectInstance(MobEffects.REGENERATION, 180)) : List.of();
    }
    public static int rows(int screenHeight, float scale, int count) {
        return Math.min(count, Math.max(1, (int)((screenHeight / scale - 16) / 24)));
    }
    public static int width(HudModules.Element element, Minecraft mc) {
        if (element == HudModules.Element.EFFECTS) return 160;
        int width = 92;
        if (mc != null && mc.font != null) for (var stack : heldItems(mc, true))
            width = Math.max(width, 24 + mc.font.width(durability(stack.getMaxDamage(), stack.getDamageValue())));
        return width;
    }
    public static int height(HudModules.Element element, Minecraft mc, int screenHeight) {
        if (element == HudModules.Element.HELD_ITEM) return Math.max(1, heldItems(mc, true).size()) * 22;
        return Math.max(1, rows(screenHeight, HudModules.scale(element), effects(mc, true).size())) * 24;
    }
    public static void draw(GuiGraphicsExtractor g, Minecraft mc, HudModules.Element element, boolean preview, int alpha, int width) {
        if (element == HudModules.Element.HELD_ITEM) {
            var items = heldItems(mc, preview);
            for (int i = 0; i < items.size(); i++) {
                var stack = items.get(i); int y = i * 22;
                g.item(stack, 0, y);
                int color = HudModules.armorBarColor(stack.getMaxDamage(), stack.getDamageValue());
                int bar = HudModules.armorBarWidth(stack.getMaxDamage(), stack.getDamageValue());
                if (bar > 0) g.fill(0, y + 16, bar, y + 17, alpha | color);
                g.text(mc.font, durability(stack.getMaxDamage(), stack.getDamageValue()), 23, y + 4, alpha | color, true);
            }
        } else {
            var effects = effects(mc, preview);
            int visible = rows(g.guiHeight(), HudModules.scale(element), effects.size());
            for (int i = 0; i < visible; i++) {
                var effect = effects.get(i); int y = i * 24;
                g.blitSprite(RenderPipelines.GUI_TEXTURED, Hud.getMobEffectSprite(effect.getEffect()), 0, y + 2, 18, 18, alpha | 0xFFFFFF);
                String label = effect.getEffect().value().getDisplayName().getString();
                if (effect.getAmplifier() > 0) label += " " + (effect.getAmplifier() + 1);
                g.text(mc.font, mc.font.plainSubstrByWidth(label, width - 24), 24, y, alpha | 0xFFFFFF, true);
                int color = !effect.isInfiniteDuration() && effect.getDuration() <= 200 ? 0xFF7777 : 0xCFD3DC;
                g.text(mc.font, duration(effect.getDuration(), effect.isInfiniteDuration()), 24, y + 11, alpha | color, true);
            }
        }
    }
}
