package dev.vortex.client.mixin;
import com.mojang.blaze3d.platform.Window;
import dev.vortex.client.ClientNotifications;
import net.minecraft.client.DeltaTracker;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.renderer.GameRenderer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;
@Mixin(GameRenderer.class)
public abstract class NotificationOverlayMixin {
    @Redirect(method="extractGui(Lnet/minecraft/client/DeltaTracker;ZZ)V",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/GuiGraphicsExtractor;applyCursor(Lcom/mojang/blaze3d/platform/Window;)V"))
    private void vortex$notifications(GuiGraphicsExtractor g,Window window,DeltaTracker delta,boolean hud,boolean screen){
        ClientNotifications.draw(g,hud,screen);g.applyCursor(window);
    }
}
