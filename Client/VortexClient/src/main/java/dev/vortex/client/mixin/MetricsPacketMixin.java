package dev.vortex.client.mixin;

import dev.vortex.client.MetricsHud;
import net.minecraft.client.multiplayer.ClientPacketListener;
import net.minecraft.network.protocol.game.ClientboundSetTimePacket;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ClientPacketListener.class)
public abstract class MetricsPacketMixin {
    @Inject(method = "handleSetTime(Lnet/minecraft/network/protocol/game/ClientboundSetTimePacket;)V", at = @At("TAIL"))
    private void vortex$serverTime(ClientboundSetTimePacket packet, CallbackInfo callback) {
        MetricsHud.serverTime(this, packet.gameTime());
    }
}
