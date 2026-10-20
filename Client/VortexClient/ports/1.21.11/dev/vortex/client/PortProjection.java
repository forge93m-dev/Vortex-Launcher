package dev.vortex.client;
import dev.vortex.client.mixin.ProjectionAccessor;
import net.minecraft.client.Camera;
import net.minecraft.client.Minecraft;
import org.joml.Matrix4f;
import org.joml.Quaternionf;
public final class PortProjection {
    private PortProjection() { }
    public static Matrix4f matrix(Camera camera,Matrix4f target) {
        var mc=Minecraft.getInstance();
        float partial=mc.getDeltaTracker().getGameTimeDeltaPartialTick(true);
        float fov=((ProjectionAccessor)(Object)mc.gameRenderer).vortex$fov(camera,partial,true);
        return target.set(mc.gameRenderer.getProjectionMatrix(fov)).rotate(camera.rotation().conjugate(new Quaternionf()));
    }
}
