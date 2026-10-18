package dev.vortex.client.mixin;
import dev.vortex.client.ShulkerPreview;
import net.minecraft.client.gui.screens.inventory.tooltip.ClientTooltipComponent;
import net.minecraft.world.inventory.tooltip.TooltipComponent;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(ClientTooltipComponent.class)
public interface ShulkerTooltipMixin {
    @Inject(method="create(Lnet/minecraft/world/inventory/tooltip/TooltipComponent;)Lnet/minecraft/client/gui/screens/inventory/tooltip/ClientTooltipComponent;",at=@At("HEAD"),cancellable=true)
    private static void vortex$grid(TooltipComponent image,CallbackInfoReturnable<ClientTooltipComponent> callback){
        if(image instanceof ShulkerPreview.Contents contents)callback.setReturnValue(new ShulkerPreview.Grid(contents));
    }
}
