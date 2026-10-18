package dev.vortex.client.mixin;

import dev.vortex.client.VortexClient;
import net.minecraft.client.KeyMapping;
import net.minecraft.client.ToggleKeyMapping;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;
import java.util.function.BooleanSupplier;

@Mixin(ToggleKeyMapping.class)
public abstract class ToggleSprintMixin {
    // Preserve vanilla input, rebinding, hunger checks and screen/focus handling.
    // Never change or save the user's vanilla accessibility option.
    @Redirect(method = {"setDown", "release", "shouldSetOnIngameFocus", "shouldRestoreStateOnScreenClosed"},
        at = @At(value = "INVOKE", target = "Ljava/util/function/BooleanSupplier;getAsBoolean()Z"))
    private boolean vortex$toggleSprint(BooleanSupplier vanilla) {
        return VortexClient.useToggleSprint(((KeyMapping)(Object)this).getName(), vanilla.getAsBoolean());
    }
}
