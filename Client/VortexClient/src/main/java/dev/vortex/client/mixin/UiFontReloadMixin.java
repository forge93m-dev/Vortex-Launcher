package dev.vortex.client.mixin;

import dev.vortex.client.UiFonts;
import net.minecraft.client.gui.font.FontManager;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(FontManager.class)
public abstract class UiFontReloadMixin {
    @Inject(method = "apply(Lnet/minecraft/client/gui/font/FontManager$Preparation;Lnet/minecraft/util/profiling/ProfilerFiller;)V", at = @At("TAIL"))
    private void vortex$fontReload(CallbackInfo callback) { UiFonts.invalidate(); }
}
