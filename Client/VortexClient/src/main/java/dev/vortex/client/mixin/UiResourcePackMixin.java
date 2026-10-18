package dev.vortex.client.mixin;

import java.util.function.BiConsumer;
import java.util.function.Function;
import dev.vortex.client.UiResources;
import net.minecraft.client.resources.ClientPackSource;
import net.minecraft.server.packs.repository.Pack;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Only the client repository: never inject a data pack into worlds or servers. */
@Mixin(ClientPackSource.class)
public abstract class UiResourcePackMixin {
    @Inject(method = "populatePackList(Ljava/util/function/BiConsumer;)V", at = @At("TAIL"))
    private void vortex$resources(BiConsumer<String, Function<String, Pack>> packs, CallbackInfo callback) {
        packs.accept("vortex_client_ui", ignored -> UiResources.createPack());
    }
}
