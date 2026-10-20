package dev.vortex.client.mixin;
import dev.vortex.client.Fullbright;
import dev.vortex.client.VortexClient;
import net.minecraft.client.renderer.LightTexture;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.ModifyArg;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(LightTexture.class)
public abstract class FullbrightMixin {
    @Shadow private boolean updateLightTexture;
    @Unique private boolean vortex$wasEnabled;
    @Inject(method="updateLightTexture(F)V",at=@At("HEAD"))
    private void vortex$refresh(float partial,CallbackInfo ci) {
        if(vortex$wasEnabled!=VortexClient.config.fullbrightEnabled) {
            updateLightTexture=true;vortex$wasEnabled=VortexClient.config.fullbrightEnabled;
        }
    }
    @ModifyArg(method="updateLightTexture(F)V",at=@At(value="INVOKE",target="Lcom/mojang/blaze3d/buffers/Std140Builder;putFloat(F)Lcom/mojang/blaze3d/buffers/Std140Builder;",ordinal=3),index=0)
    private float vortex$nightVision(float original){return Fullbright.brighten(original);}
    @ModifyArg(method="updateLightTexture(F)V",at=@At(value="INVOKE",target="Lcom/mojang/blaze3d/buffers/Std140Builder;putFloat(F)Lcom/mojang/blaze3d/buffers/Std140Builder;",ordinal=6),index=0)
    private float vortex$brightness(float original){return Fullbright.brighten(original);}
}
