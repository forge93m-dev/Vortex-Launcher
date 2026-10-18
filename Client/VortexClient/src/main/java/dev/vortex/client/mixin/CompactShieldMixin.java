package dev.vortex.client.mixin;
import com.mojang.blaze3d.vertex.PoseStack;
import dev.vortex.client.CompactShield;
import net.minecraft.client.renderer.FirstPersonHandsAndItemsRenderer;
import net.minecraft.client.renderer.SubmitNodeCollector;
import net.minecraft.client.renderer.state.level.PlayerRenderState;
import net.minecraft.client.renderer.state.level.FirstPersonHandsAndItemsRenderState;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.item.ItemStack;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(FirstPersonHandsAndItemsRenderer.class)
public abstract class CompactShieldMixin {
    @Inject(method="submitArmWithItem",at=@At("HEAD"))
    private void vortex$small(PlayerRenderState player,FirstPersonHandsAndItemsRenderState state,float partial,float pitch,InteractionHand hand,float swing,ItemStack stack,float height,PoseStack pose,SubmitNodeCollector collector,int light,CallbackInfo callback){
        pose.pushPose();float scale=CompactShield.scale(stack);if(scale<1){pose.translate(0,-(1-scale)*.55,0);pose.scale(scale,scale,scale);}
    }
    @Inject(method="submitArmWithItem",at=@At("RETURN"))
    private void vortex$restore(PlayerRenderState player,FirstPersonHandsAndItemsRenderState state,float partial,float pitch,InteractionHand hand,float swing,ItemStack stack,float height,PoseStack pose,SubmitNodeCollector collector,int light,CallbackInfo callback){pose.popPose();}
}
