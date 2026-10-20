package dev.vortex.client.mixin;
import com.mojang.blaze3d.platform.Window;
import dev.vortex.client.ClientNotifications;
import dev.vortex.client.VortexClient;
import net.minecraft.client.DeltaTracker;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.renderer.GameRenderer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;
@Mixin(GameRenderer.class)
public abstract class NotificationOverlayMixin {
    @Redirect(method="render(Lnet/minecraft/client/DeltaTracker;Z)V",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/GuiGraphics;applyCursor(Lcom/mojang/blaze3d/platform/Window;)V"))
    private void vortex$notifications(GuiGraphics g,Window window,DeltaTracker delta,boolean renderLevel){
        ClientNotifications.draw(g,true,true);g.applyCursor(window);
    }
    @Inject(method="getFov",at=@At("RETURN"),cancellable=true)
    private void vortex$zoom(CallbackInfoReturnable<Float> ci){ci.setReturnValue(VortexClient.zoom(ci.getReturnValue()));}
}
