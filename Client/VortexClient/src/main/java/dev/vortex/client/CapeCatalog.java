package dev.vortex.client;

import java.util.Map;
import net.minecraft.core.ClientAsset;
import net.minecraft.resources.Identifier;
import net.minecraft.world.entity.player.PlayerSkin;

/** Bundled textures or checksum-verified pixels from the pinned Vortex service. */
public final class CapeCatalog {
    public static final String[] IDS = {"none", "vortex", "aurora", "ember", "nebula", "flow", "pulse"};
    /** Animated capes: frame count and milliseconds per frame. Frames are textures/capes/<id>_<n>.png. */
    private static final Map<String, int[]> ANIMATED = Map.of("nebula", new int[] {16, 120}, "flow", new int[] {12, 100}, "pulse", new int[] {12, 110});
    private static final Map<String, ClientAsset.Texture[]> FRAMES = new java.util.HashMap<>();
    /**
     * Whether the cape services (cosmetics and accounts workers) have been updated to accept the animated ids.
     * Until both are deployed, sending one would only fail, so animated capes stay on this computer.
     * Flip to true together with the service deployment described in Services/VortexCosmetics/DEPLOYMENT.md.
     */
    public static final boolean ANIMATED_ONLINE = false;
    public static boolean animated(String id) { return id != null && ANIMATED.containsKey(id); }
    /** Frame shown at a given time; every player wearing the cape animates in step. */
    public static int frameAt(String id, long millis) {
        int[] timing = ANIMATED.get(id);
        return timing == null ? 0 : (int)Math.floorMod(millis / timing[1], (long)timing[0]);
    }
    /** First frame of an animated cape for any of its frames (so a fade sees one cape, not a new one per frame). */
    public static ClientAsset.Texture canonical(ClientAsset.Texture texture) {
        if (texture == null) return null;
        for (var entry : FRAMES.entrySet())
            for (var frame : entry.getValue()) if (frame.equals(texture)) return entry.getValue()[0];
        return texture;
    }
    /** The current frame for a cape given by its first frame; other textures are returned unchanged. */
    public static ClientAsset.Texture current(ClientAsset.Texture texture) {
        if (texture == null) return null;
        for (var entry : FRAMES.entrySet()) if (entry.getValue()[0].equals(texture)) return frame(entry.getKey());
        return texture;
    }
    private static ClientAsset.Texture frame(String id) {
        var frames = FRAMES.computeIfAbsent(id, key -> {
            var result = new ClientAsset.Texture[ANIMATED.get(key)[0]];
            for (int i = 0; i < result.length; i++) result[i] = asset(key + "_" + i);
            return result;
        });
        return frames[frameAt(id, System.currentTimeMillis())];
    }
    private static final Map<String, ClientAsset.Texture> TEXTURES = Map.of(
        "vortex", asset("vortex"), "aurora", asset("aurora"), "ember", asset("ember"));
    private CapeCatalog() { }
    private static ClientAsset.Texture asset(String id) {
        return new ClientAsset.ResourceTexture(Identifier.fromNamespaceAndPath("vortex_client", "capes/" + id),
            Identifier.fromNamespaceAndPath("vortex_client", "textures/capes/" + id + ".png"));
    }
    public static boolean custom(String id) {return id!=null && id.matches("(?:custom|hd):[a-f0-9]{64}");}
    public static boolean valid(String id) { return "none".equals(id) || custom(id) || animated(id) || TEXTURES.containsKey(id == null ? "" : id); }
    private static ClientAsset.Texture assetFor(String id) {return custom(id) ? CapeImages.asset(id) : animated(id) ? frame(id) : TEXTURES.get(id == null ? "" : id);}
    public static Identifier texture(String id) { var asset = assetFor(id); return asset == null ? null : asset.texturePath(); }
    public static PlayerSkin patch(PlayerSkin original, String id) {
        var cape = assetFor(id);
        return original == null || cape == null ? original : new PlayerSkin(original.body(), cape,
            original.elytra(), original.model(), original.secure());
    }
}
