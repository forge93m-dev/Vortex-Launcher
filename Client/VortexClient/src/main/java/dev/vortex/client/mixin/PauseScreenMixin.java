package dev.vortex.client.mixin;

import dev.vortex.client.VortexMenuChrome;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.PauseScreen;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(PauseScreen.class)
public abstract class PauseScreenMixin extends Screen {
    @Unique private VortexMenuChrome vortex$menu;
    protected PauseScreenMixin(Component title) { super(title); }
    @Inject(method = "init()V", at = @At("TAIL"))
    private void vortex$init(CallbackInfo callback) {
        if (!((PauseScreen)(Object)this).showsPauseMenu()) return;
        var buttons = children().stream().filter(Button.class::isInstance).map(Button.class::cast).toList();
        clearWidgets();
        vortex$menu = new VortexMenuChrome((Screen)(Object)this, true, buttons);
        for (var button : vortex$menu.widgets(width, height)) addRenderableWidget(button);
    }
    @Inject(method = "extractRenderState(Lnet/minecraft/client/gui/GuiGraphicsExtractor;IIF)V", at = @At("HEAD"), cancellable = true)
    private void vortex$render(GuiGraphicsExtractor g, int mx, int my, float delta, CallbackInfo callback) {
        if (vortex$menu == null) return;
        // Dim only the pause menu. Do not restyle the underlying in-game HUD.
        vortex$menu.draw(g, width, height);
        super.extractRenderState(g, mx, my, delta);
        callback.cancel();
    }
}
