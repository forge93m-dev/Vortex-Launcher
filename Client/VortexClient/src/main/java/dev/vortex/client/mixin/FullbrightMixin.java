package dev.vortex.client.mixin;
import dev.vortex.client.Fullbright;
import dev.vortex.client.VortexClient;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.LightmapRenderStateExtractor;
import net.minecraft.client.renderer.state.LightmapRenderState;
import net.minecraft.world.effect.MobEffects;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(LightmapRenderStateExtractor.class)
public abstract class FullbrightMixin {
    @Shadow private boolean needsUpdate;
    @Unique private boolean vortex$wasEnabled;
    @Inject(method = "extract(Lnet/minecraft/client/renderer/state/LightmapRenderState;F)V", at = @At("HEAD"))
    private void vortex$refresh(LightmapRenderState state, float partialTick, CallbackInfo callback) {
        if (vortex$wasEnabled != VortexClient.config.fullbrightEnabled) {
            needsUpdate = true; vortex$wasEnabled = VortexClient.config.fullbrightEnabled;
        }
    }
    @Inject(method = "extract(Lnet/minecraft/client/renderer/state/LightmapRenderState;F)V", at = @At("TAIL"))
    private void vortex$fullbright(LightmapRenderState state, float partialTick, CallbackInfo callback) {
        var mc = Minecraft.getInstance();
        boolean allowed = VortexClient.config.fullbrightEnabled && mc != null && mc.level != null && mc.player != null &&
            !mc.player.hasEffect(MobEffects.DARKNESS) && !mc.player.hasEffect(MobEffects.BLINDNESS);
        Fullbright.apply(state, allowed);
    }
}
