package dev.vortex.client;
import com.mojang.blaze3d.vertex.PoseStack;
import net.minecraft.client.model.Model;
import net.minecraft.client.renderer.SubmitNodeCollector;
import net.minecraft.client.renderer.rendertype.RenderType;
import net.minecraft.client.renderer.rendertype.RenderTypes;
import net.minecraft.client.renderer.feature.ModelFeatureRenderer.CrumblingOverlay;
public final class WardrobeCapeRendering {
    private WardrobeCapeRendering() { }
    public static <S> void submit(SubmitNodeCollector collector,Model<? super S> model,S state,PoseStack pose,
                                  RenderType type,int light,int overlay,int outline,CrumblingOverlay crumbling) {
        if(state instanceof WardrobePreview.PreviewState preview && preview.capeOpacity<1) {
            int alpha=Math.round(Math.clamp(preview.capeOpacity,0,1)*255);
            if(alpha==0 || preview.skin==null || preview.skin.cape()==null)return;
            collector.submitModel(model,state,pose,RenderTypes.entityTranslucent(preview.skin.cape().texturePath()),
                light,overlay,(alpha<<24)|0xFFFFFF,null,outline,crumbling);
        } else collector.submitModel(model,state,pose,type,light,overlay,outline,crumbling);
    }
}
