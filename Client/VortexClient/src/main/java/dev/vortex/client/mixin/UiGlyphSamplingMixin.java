package dev.vortex.client.mixin;

import com.mojang.renderpearl.api.textures.FilterMode;
import dev.vortex.client.UiSampling;
import net.minecraft.client.gui.font.TextRenderable;
import net.minecraft.client.renderer.state.gui.GlyphRenderState;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyArg;

@Mixin(GlyphRenderState.class)
public abstract class UiGlyphSamplingMixin {
    @Shadow @Final private TextRenderable renderable;

    @ModifyArg(method = "textureSetup", at = @At(value = "INVOKE",
        target = "Lcom/mojang/blaze3d/systems/SamplerCache;getClampToEdge(Lcom/mojang/renderpearl/api/textures/FilterMode;)Lcom/mojang/renderpearl/api/textures/GpuSampler;"), index = 0)
    private FilterMode vortex$fontFilter(FilterMode original) {
        var view = renderable.textureView();
        return view == null ? original : UiSampling.fontFilter(view.texture().getLabel(), original);
    }
}
