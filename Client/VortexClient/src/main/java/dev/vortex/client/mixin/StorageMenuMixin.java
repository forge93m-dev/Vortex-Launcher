package dev.vortex.client.mixin;
import dev.vortex.client.StorageMemory;
import net.minecraft.world.inventory.AbstractContainerMenu;
import net.minecraft.world.item.ItemStack;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(AbstractContainerMenu.class)
public abstract class StorageMenuMixin {
    @Inject(method="initializeContents(ILjava/util/List;Lnet/minecraft/world/item/ItemStack;)V",at=@At("TAIL"))
    private void vortex$storageContents(int state,java.util.List<ItemStack> items,ItemStack carried,CallbackInfo callback){StorageMemory.received((AbstractContainerMenu)(Object)this,true);}
    @Inject(method="setItem(IILnet/minecraft/world/item/ItemStack;)V",at=@At("TAIL"))
    private void vortex$storageSlot(int slot,int state,ItemStack stack,CallbackInfo callback){StorageMemory.received((AbstractContainerMenu)(Object)this,false);}
}
