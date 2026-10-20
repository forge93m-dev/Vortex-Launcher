package dev.vortex.client.mixin;
import com.mojang.blaze3d.vertex.PoseStack;
import dev.vortex.client.WardrobeCapeRendering;
import net.minecraft.client.model.Model;
import net.minecraft.client.renderer.SubmitNodeCollector;
import net.minecraft.client.renderer.entity.layers.CapeLayer;
import net.minecraft.client.renderer.rendertype.RenderType;
import net.minecraft.client.renderer.feature.ModelFeatureRenderer.CrumblingOverlay;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;
@Mixin(CapeLayer.class)
public abstract class WardrobeCapeFadeMixin {
    @Redirect(method="submit(Lcom/mojang/blaze3d/vertex/PoseStack;Lnet/minecraft/client/renderer/SubmitNodeCollector;ILnet/minecraft/client/renderer/entity/state/AvatarRenderState;FF)V",
        at=@At(value="INVOKE",target="Lnet/minecraft/client/renderer/SubmitNodeCollector;submitModel(Lnet/minecraft/client/model/Model;Ljava/lang/Object;Lcom/mojang/blaze3d/vertex/PoseStack;Lnet/minecraft/client/renderer/rendertype/RenderType;IIILnet/minecraft/client/renderer/feature/ModelFeatureRenderer$CrumblingOverlay;)V"))
    private <S> void vortex$fade(SubmitNodeCollector collector,Model<? super S> model,S state,PoseStack pose,
                                RenderType type,int light,int overlay,int outline,CrumblingOverlay crumbling) {
        WardrobeCapeRendering.submit(collector,model,state,pose,type,light,overlay,outline,crumbling);
    }
}
