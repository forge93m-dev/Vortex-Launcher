package dev.vortex.client.mixin;

import dev.vortex.client.AccountSkinTextures;
import dev.vortex.client.LocalSkins;
import dev.vortex.client.SkinCalls;
import net.minecraft.client.multiplayer.PlayerInfo;
import net.minecraft.world.entity.player.PlayerSkin;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Skins read straight from the tab-list entry (chat heads, name-tag and other mods) get the same Vortex skin as the
 * player entity. Offline accounts have no Mojang skin, so without this they would show the default Steve or Alex.
 */
@Mixin(PlayerInfo.class)
public abstract class PlayerInfoSkinMixin {
    @Inject(method="getSkin", at=@At("RETURN"), cancellable=true)
    private void vortex$skin(CallbackInfoReturnable<PlayerSkin> callback) {
        if (SkinCalls.inside()) return; // the player entity applies the override itself
        var original = callback.getReturnValue();
        var id = ((PlayerInfo)(Object)this).getProfile().id();
        if (original == null || id == null) return;
        var changed = AccountSkinTextures.skin(id, LocalSkins.skin(id, original));
        if (changed != original) callback.setReturnValue(changed);
    }
}
