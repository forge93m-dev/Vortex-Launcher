package dev.vortex.client.mixin;

import dev.vortex.client.Capes;
import net.minecraft.client.player.AbstractClientPlayer;
import net.minecraft.world.entity.player.PlayerSkin;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(AbstractClientPlayer.class)
public abstract class CapeSkinMixin {
    @Inject(method="getSkin", at=@At("RETURN"), cancellable=true)
    private void vortex$cape(CallbackInfoReturnable<PlayerSkin> callback) {
        // WingsLayer can otherwise fall back to the custom cape as its elytra
        // texture. Keep the entire vanilla skin while gliding gear is equipped.
        var player = (AbstractClientPlayer)(Object)this;
        var original = callback.getReturnValue();
        var localSkin = dev.vortex.client.LocalSkins.skin(player.getUUID(),original);
        localSkin = dev.vortex.client.AccountSkinTextures.skin(player.getUUID(),localSkin);
        var changed = player.getItemBySlot(net.minecraft.world.entity.EquipmentSlot.CHEST).has(net.minecraft.core.component.DataComponents.GLIDER)
            ? localSkin : Capes.skin(player.getUUID(),localSkin);
        if (changed != original) callback.setReturnValue(changed);
    }
}
