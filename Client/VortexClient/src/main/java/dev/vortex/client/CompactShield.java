package dev.vortex.client;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
public final class CompactShield {
    public static float scale(ItemStack item){return VortexClient.config.compactShieldEnabled && item.is(Items.SHIELD)?Math.clamp(VortexClient.config.shieldScale,.5f,1f):1f;}
}
