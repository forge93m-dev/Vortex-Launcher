package dev.vortex.client;

import java.util.Map;
import net.minecraft.core.ClientAsset;
import net.minecraft.resources.Identifier;
import net.minecraft.world.entity.player.PlayerSkin;

/** Bundled textures or checksum-verified pixels from the pinned Vortex service. */
public final class CapeCatalog {
    public static final String[] IDS = {"none", "vortex", "aurora", "ember"};
    private static final Map<String, ClientAsset.Texture> TEXTURES = Map.of(
        "vortex", asset("vortex"), "aurora", asset("aurora"), "ember", asset("ember"));
    private CapeCatalog() { }
    private static ClientAsset.Texture asset(String id) {
        return new ClientAsset.ResourceTexture(Identifier.fromNamespaceAndPath("vortex_client", "capes/" + id),
            Identifier.fromNamespaceAndPath("vortex_client", "textures/capes/" + id + ".png"));
    }
    public static boolean custom(String id) {return id!=null && id.matches("(?:custom|hd):[a-f0-9]{64}");}
    public static boolean valid(String id) { return "none".equals(id) || custom(id) || TEXTURES.containsKey(id == null ? "" : id); }
    private static ClientAsset.Texture assetFor(String id) {return custom(id) ? CapeImages.asset(id) : TEXTURES.get(id == null ? "" : id);}
    public static Identifier texture(String id) { var asset = assetFor(id); return asset == null ? null : asset.texturePath(); }
    public static PlayerSkin patch(PlayerSkin original, String id) {
        var cape = assetFor(id);
        return original == null || cape == null ? original : new PlayerSkin(original.body(), cape,
            original.elytra(), original.model(), original.secure());
    }
}
