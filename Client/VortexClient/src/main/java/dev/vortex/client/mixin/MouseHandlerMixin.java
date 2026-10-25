package dev.vortex.client.mixin;

import dev.vortex.client.VortexClient;
import dev.vortex.client.Freelook;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.client.MouseHandler;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(MouseHandler.class)
public abstract class MouseHandlerMixin {
    @Redirect(method = "turnPlayer(D)V", at = @At(value = "INVOKE", target = "Lnet/minecraft/client/player/LocalPlayer;turn(DD)V"))
    private void vortex$freelook(LocalPlayer player, double dx, double dy) {
        // Vanilla has already applied sensitivity, cinematic smoothing and invert settings.
        if (!Freelook.turn(player, dx, dy)) {
            double scale = VortexClient.zoomTurnScale();
            player.turn(dx * scale, dy * scale);
        }
    }
    @Inject(method = "onScroll(JDD)V", at = @At("HEAD"), cancellable = true)
    private void vortex$scroll(long window, double horizontal, double vertical, CallbackInfo callback) {
        if (VortexClient.scrollZoom(window, vertical)) callback.cancel();
    }
}
