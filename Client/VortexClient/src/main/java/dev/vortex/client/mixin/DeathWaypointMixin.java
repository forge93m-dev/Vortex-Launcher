package dev.vortex.client.mixin;
import dev.vortex.client.DeathWaypoints;
import net.minecraft.client.Minecraft;
import net.minecraft.client.multiplayer.ClientPacketListener;
import net.minecraft.network.protocol.game.ClientboundSetHealthPacket;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(ClientPacketListener.class)
public abstract class DeathWaypointMixin {
    @Inject(method="handleSetHealth",at=@At("TAIL"))
    private void vortex$death(ClientboundSetHealthPacket packet,CallbackInfo callback){if(packet.getHealth()<=0)DeathWaypoints.healthDepleted(Minecraft.getInstance());}
}
