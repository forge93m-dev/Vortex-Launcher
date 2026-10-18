package dev.vortex.client;

import com.mojang.renderpearl.api.textures.FilterMode;

/** Only our oversampled menu fonts; vanilla text and every other texture stay pixel-exact. */
public final class UiSampling {
    private UiSampling() { }
    public static FilterMode fontFilter(String label, FilterMode original) {
        return label != null && (label.startsWith("vortex_client:ui/") || label.startsWith("vortex_client:heading/"))
            ? FilterMode.LINEAR : original;
    }
}
