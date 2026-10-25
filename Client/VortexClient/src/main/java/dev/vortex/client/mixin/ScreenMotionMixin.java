package dev.vortex.client.mixin;

import dev.vortex.client.MenuMotion;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.inventory.AbstractContainerScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(Screen.class)
public abstract class ScreenMotionMixin {
    @Shadow public int width;
    @Shadow public int height;
    /** The in-game menu dim fades in instead of appearing at once. */
    @Inject(method = "extractTransparentBackground", at = @At("HEAD"), cancellable = true)
    private void vortex$softDim(GuiGraphicsExtractor g, CallbackInfo callback) {
        MenuMotion.dim(g, (Screen)(Object)this, width, height);
        callback.cancel();
    }
    /** Container panels (inventory, chests...) ease into place; the dim behind them is drawn before this. */
    @Inject(method = "extractRenderStateWithTooltipAndSubtitles", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/screens/Screen;extractRenderState(Lnet/minecraft/client/gui/GuiGraphicsExtractor;IIF)V"))
    private void vortex$panelIn(GuiGraphicsExtractor g, int mx, int my, float delta, CallbackInfo callback) {
        if ((Object)this instanceof AbstractContainerScreen<?>) MenuMotion.beginPanel(g, (Screen)(Object)this, width, height);
    }
    @Inject(method = "extractRenderStateWithTooltipAndSubtitles", at = @At(value = "INVOKE", shift = At.Shift.AFTER,
        target = "Lnet/minecraft/client/gui/screens/Screen;extractRenderState(Lnet/minecraft/client/gui/GuiGraphicsExtractor;IIF)V"))
    private void vortex$panelDone(GuiGraphicsExtractor g, int mx, int my, float delta, CallbackInfo callback) {
        if ((Object)this instanceof AbstractContainerScreen<?>) MenuMotion.endPanel(g);
    }
}
