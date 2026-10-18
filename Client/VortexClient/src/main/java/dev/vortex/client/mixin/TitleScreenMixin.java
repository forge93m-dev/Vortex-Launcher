package dev.vortex.client.mixin;

import dev.vortex.client.VortexMenuChrome;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.network.chat.Component;
import net.minecraft.client.input.MouseButtonEvent;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(TitleScreen.class)
public abstract class TitleScreenMixin extends Screen {
    @Unique private VortexMenuChrome vortex$menu;
    protected TitleScreenMixin(Component title) { super(title); }
    @Inject(method = "init()V", at = @At("TAIL"))
    private void vortex$init(CallbackInfo callback) {
        var buttons = children().stream().filter(Button.class::isInstance).map(Button.class::cast).toList();
        clearWidgets();
        vortex$menu = new VortexMenuChrome((Screen)(Object)this, false, buttons);
        for (var button : vortex$menu.widgets(width, height)) addRenderableWidget(button);
    }
    @Inject(method = "extractRenderState(Lnet/minecraft/client/gui/GuiGraphicsExtractor;IIF)V", at = @At("HEAD"), cancellable = true)
    private void vortex$render(GuiGraphicsExtractor g, int mx, int my, float delta, CallbackInfo callback) {
        if (vortex$menu == null) return;
        // Reuse Minecraft's panorama once, without a second fullscreen blur pass.
        extractPanorama(g, delta);
        vortex$menu.draw(g, width, height);
        super.extractRenderState(g, mx, my, delta);
        callback.cancel();
    }
    @Inject(method = "mouseClicked(Lnet/minecraft/client/input/MouseButtonEvent;Z)Z", at = @At("HEAD"), cancellable = true)
    private void vortex$click(MouseButtonEvent event, boolean doubleClick, CallbackInfoReturnable<Boolean> callback) {
        // Do not let the old, now-hidden Realms notification overlay handle background clicks.
        if (vortex$menu != null) callback.setReturnValue(super.mouseClicked(event, doubleClick));
    }
}
