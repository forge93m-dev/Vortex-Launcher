package dev.vortex.client;
import net.minecraft.client.Minecraft;
import net.minecraft.world.effect.MobEffects;
/** Overrides only the current frame's light uniforms, never stored options or effects. */
public final class Fullbright {
    private Fullbright() { }
    public static boolean allowed() {
        var mc=Minecraft.getInstance();
        return VortexClient.config.fullbrightEnabled && mc!=null && mc.level!=null && mc.player!=null &&
            !mc.player.hasEffect(MobEffects.DARKNESS) && !mc.player.hasEffect(MobEffects.BLINDNESS);
    }
    public static float brighten(float original) { return allowed()?Math.max(1,original):original; }
}
