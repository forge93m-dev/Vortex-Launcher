package dev.vortex.client.mixin;

import dev.vortex.client.VortexClient;
import net.minecraft.client.DeltaTracker;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.Hud;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(Hud.class)
public abstract class HudMixin {
    @Redirect(method = "extractCrosshair(Lnet/minecraft/client/gui/GuiGraphicsExtractor;Lnet/minecraft/client/DeltaTracker;)V",
        at = @At(value = "INVOKE", target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;blitSprite(Lcom/mojang/renderpearl/api/pipeline/RenderPipeline;Lnet/minecraft/resources/Identifier;IIII)V", ordinal = 0))
    private void vortex$crosshair(GuiGraphicsExtractor g, com.mojang.renderpearl.api.pipeline.RenderPipeline pipeline,
                                 net.minecraft.resources.Identifier sprite, int x, int y, int width, int height) {
        // Replace only the crosshair sprite. Keep vanilla camera/spectator checks and attack indicator.
        if (VortexClient.config.crosshairEnabled) dev.vortex.client.CustomCrosshair.draw(g, g.guiWidth() / 2, g.guiHeight() / 2);
        else g.blitSprite(pipeline, sprite, x, y, width, height);
    }
    @Inject(method = "extractEffects(Lnet/minecraft/client/gui/GuiGraphicsExtractor;Lnet/minecraft/client/DeltaTracker;)V", at = @At("HEAD"), cancellable = true)
    private void vortex$effects(GuiGraphicsExtractor g, DeltaTracker delta, CallbackInfo callback) {
        if (VortexClient.config.effectsEnabled) callback.cancel();
    }
    @Inject(method = "extractRenderState(Lnet/minecraft/client/gui/GuiGraphicsExtractor;Lnet/minecraft/client/DeltaTracker;)V", at = @At("TAIL"))
    private void vortex$hud(GuiGraphicsExtractor graphics, DeltaTracker delta, CallbackInfo callback) {
        // The editor renders its own previews; avoid two copies of each overlay.
        if (net.minecraft.client.Minecraft.getInstance().gui.screen() instanceof dev.vortex.client.HudEditorScreen) return;
        VortexClient.drawFps(graphics, false);
        VortexClient.drawZoom(graphics);
        dev.vortex.client.HudModules.draw(graphics, false);
        dev.vortex.client.DurabilityWarnings.draw(graphics);
        dev.vortex.client.PartyFeature.draw(graphics);
        dev.vortex.client.WaypointsFeature.draw(graphics);
        dev.vortex.client.EntityHearts.draw(graphics,delta);
    }
}
