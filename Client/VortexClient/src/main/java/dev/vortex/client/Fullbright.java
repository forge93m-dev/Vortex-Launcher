package dev.vortex.client;
import net.minecraft.client.renderer.state.LightmapRenderState;
/** Local lighting only: no effects, server light changes or saved gamma writes. */
public final class Fullbright {
    private Fullbright() { }
    public static void apply(LightmapRenderState state, boolean enabled) {
        if (!enabled) return;
        if (state.nightVisionEffectIntensity < 1 || state.brightness < 1) state.needsUpdate = true;
        state.nightVisionEffectIntensity = Math.max(1, state.nightVisionEffectIntensity);
        state.brightness = Math.max(1, state.brightness);
    }
}
