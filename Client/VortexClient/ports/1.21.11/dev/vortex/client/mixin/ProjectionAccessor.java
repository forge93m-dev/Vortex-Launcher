package dev.vortex.client.mixin;
import net.minecraft.client.Camera;
import net.minecraft.client.renderer.GameRenderer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Invoker;
@Mixin(GameRenderer.class)
public interface ProjectionAccessor {
    @Invoker("getFov") float vortex$fov(Camera camera,float partial,boolean useSettings);
}
