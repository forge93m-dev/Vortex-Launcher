package dev.vortex.client.mixin;

import dev.vortex.client.VortexClient;
import dev.vortex.client.Freelook;
import net.minecraft.client.Minecraft;
import net.minecraft.world.entity.Entity;
import net.minecraft.client.Camera;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(Camera.class)
public abstract class CameraMixin {
    @Inject(method = "alignWithEntity(F)V", at = @At("HEAD"))
    private void vortex$freelookSync(float partialTick, CallbackInfo callback) { Freelook.sync(Minecraft.getInstance()); }
    @Redirect(method = "alignWithEntity(F)V", at = @At(value = "INVOKE", target = "Lnet/minecraft/world/entity/Entity;getViewYRot(F)F"))
    private float vortex$freelookYaw(Entity viewed, float partialTick) {
        float vanilla = viewed.getViewYRot(partialTick);
        return viewed == Minecraft.getInstance().player ? Freelook.yaw(vanilla) : vanilla;
    }
    @Redirect(method = "alignWithEntity(F)V", at = @At(value = "INVOKE", target = "Lnet/minecraft/world/entity/Entity;getViewXRot(F)F"))
    private float vortex$freelookPitch(Entity viewed, float partialTick) {
        float vanilla = viewed.getViewXRot(partialTick);
        return viewed == Minecraft.getInstance().player ? Freelook.pitch(vanilla) : vanilla;
    }
    @Inject(method = "calculateFov(F)F", at = @At("RETURN"), cancellable = true)
    private void vortex$zoom(float partialTick, CallbackInfoReturnable<Float> callback) {
        callback.setReturnValue(VortexClient.zoom(callback.getReturnValue()));
    }
}
