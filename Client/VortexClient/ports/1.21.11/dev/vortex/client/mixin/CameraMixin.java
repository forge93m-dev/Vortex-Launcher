package dev.vortex.client.mixin;
import dev.vortex.client.Freelook;
import net.minecraft.client.Minecraft;
import net.minecraft.client.Camera;
import net.minecraft.world.entity.Entity;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(Camera.class)
public abstract class CameraMixin {
    @Inject(method="setup",at=@At("HEAD"))
    private void vortex$sync(CallbackInfo ci){Freelook.sync(Minecraft.getInstance());}
    @Redirect(method="setup",at=@At(value="INVOKE",target="Lnet/minecraft/world/entity/Entity;getViewYRot(F)F"))
    private float vortex$yaw(Entity entity,float partial){float original=entity.getViewYRot(partial);return entity==Minecraft.getInstance().player?Freelook.yaw(original):original;}
    @Redirect(method="setup",at=@At(value="INVOKE",target="Lnet/minecraft/world/entity/Entity;getViewXRot(F)F"))
    private float vortex$pitch(Entity entity,float partial){float original=entity.getViewXRot(partial);return entity==Minecraft.getInstance().player?Freelook.pitch(original):original;}
}
