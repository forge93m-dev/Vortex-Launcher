package dev.vortex.client;
import net.minecraft.client.Minecraft;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.HitResult;
/** Only Minecraft's normal visible-block raycast; never hidden blocks. */
public final class BlockInfo {
    public record Readout(String name, String id, String position, ItemStack icon) { }
    private static Object lastState;
    private static String name, id;
    private static ItemStack icon = ItemStack.EMPTY;
    private BlockInfo() { }
    public static String displayName(String path) { return HudModules.biomeName(path); }
    public static Readout readout(Minecraft mc, boolean preview) {
        if (mc != null && mc.level != null && mc.hitResult instanceof BlockHitResult hit && hit.getType() == HitResult.Type.BLOCK) {
            var state = mc.level.getBlockState(hit.getBlockPos());
            if (!state.isAir()) {
                if (lastState != state) {
                    lastState = state;
                    var key = BuiltInRegistries.BLOCK.getKey(state.getBlock());
                    id = key.toString(); name = displayName(key.getPath());
                    icon = state.getBlock().asItem().getDefaultInstance();
                }
                var pos = hit.getBlockPos();
                return new Readout(name, id, "X " + pos.getX() + " / Y " + pos.getY() + " / Z " + pos.getZ(), icon);
            }
        }
        return preview ? new Readout("Stone", "minecraft:stone", "X 209 / Y -55 / Z 860", ItemStack.EMPTY) : null;
    }
    public static void draw(net.minecraft.client.gui.GuiGraphicsExtractor g, Minecraft mc, boolean preview, int alpha, int width) {
        var info = readout(mc, preview);
        if (info == null) return;
        int textX = info.icon().isEmpty() ? 0 : 23;
        if (!info.icon().isEmpty()) g.item(info.icon(), 0, 7);
        int available = Math.max(0, width - textX);
        g.text(mc.font, mc.font.plainSubstrByWidth(info.name(), available), textX, 0, alpha | 0xFFFFFF, true);
        g.text(mc.font, mc.font.plainSubstrByWidth(info.id(), available), textX, 11, alpha | 0xB8A5FF, true);
        g.text(mc.font, mc.font.plainSubstrByWidth(info.position(), available), textX, 22, alpha | 0xBEC5D3, true);
    }
}
