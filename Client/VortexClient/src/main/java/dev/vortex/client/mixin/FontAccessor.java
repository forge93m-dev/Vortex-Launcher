package dev.vortex.client.mixin;

import net.minecraft.client.gui.Font;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

/** Read-only access; the vanilla provider and global font are never replaced. */
@Mixin(Font.class)
public interface FontAccessor {
    @Accessor("provider") Font.Provider vortex$provider();
}
