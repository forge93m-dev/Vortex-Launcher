package dev.vortex.client.mixin;

import dev.vortex.client.PlayerList;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.PlayerTabOverlay;
import net.minecraft.network.chat.Component;
import net.minecraft.world.scores.Objective;
import net.minecraft.world.scores.Scoreboard;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(PlayerTabOverlay.class)
public abstract class PlayerTabOverlayMixin {
    @Shadow private Component header;
    @Shadow private Component footer;
    /** The Vortex list replaces vanilla's; with a scoreboard column (hearts/scores) vanilla still draws. */
    @Inject(method = "extractRenderState", at = @At("HEAD"), cancellable = true)
    private void vortex$playerList(GuiGraphicsExtractor g, int width, Scoreboard scoreboard, Objective objective, CallbackInfo callback) {
        if (PlayerList.draw(g, width, objective != null, header, footer)) callback.cancel();
    }
}
