package dev.vortex.client.mixin;

import dev.vortex.client.VortexClient;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(Minecraft.class)
public abstract class MinecraftMixin {
    @Inject(method = "tick()V", at = @At("TAIL"))
    private void vortex$tick(CallbackInfo callback) { VortexClient.tick((Minecraft)(Object)this); }
}
