package dev.vortex.client.mixin;
import dev.vortex.client.EnchantmentHints;
import java.util.function.Consumer;
import net.minecraft.network.chat.Component;
import net.minecraft.world.item.enchantment.ItemEnchantments;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.TooltipFlag;
import net.minecraft.core.component.DataComponentGetter;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ItemEnchantments.class)
public abstract class EnchantmentHintsMixin {
    @Inject(method="addToTooltip",at=@At("TAIL"))
    private void vortex$hints(Item.TooltipContext context,Consumer<Component> output,TooltipFlag flag,DataComponentGetter components,CallbackInfo callback){EnchantmentHints.append((ItemEnchantments)(Object)this,output);}
}
