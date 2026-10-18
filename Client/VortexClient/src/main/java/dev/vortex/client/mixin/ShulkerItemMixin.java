package dev.vortex.client.mixin;
import java.util.Optional;
import dev.vortex.client.ShulkerPreview;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.inventory.tooltip.TooltipComponent;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(ItemStack.class)
public abstract class ShulkerItemMixin {
    @Inject(method="getTooltipImage()Ljava/util/Optional;",at=@At("RETURN"),cancellable=true)
    private void vortex$shulker(CallbackInfoReturnable<Optional<TooltipComponent>> callback){
        if(callback.getReturnValue().isEmpty())ShulkerPreview.image((ItemStack)(Object)this).ifPresent(image->callback.setReturnValue(Optional.of(image)));
    }
}
