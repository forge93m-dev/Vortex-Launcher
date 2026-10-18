package dev.vortex.client;

import java.util.List;
import java.util.Optional;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.network.chat.Component;
import net.minecraft.server.packs.PackLocationInfo;
import net.minecraft.server.packs.PackSelectionConfig;
import net.minecraft.server.packs.PathPackResources;
import net.minecraft.server.packs.repository.Pack;
import net.minecraft.server.packs.repository.PackCompatibility;
import net.minecraft.server.packs.repository.PackSource;
import net.minecraft.world.flag.FeatureFlagSet;

/** Loader alone does not expose mod assets as Minecraft resource packs. */
public final class UiResources {
    private UiResources() { }
    public static Pack createPack() {
        var root = FabricLoader.getInstance().getModContainer("vortex_client").orElseThrow()
            .findPath("assets").orElseThrow().getParent();
        var title = Component.literal("Vortex UI resources");
        var location = new PackLocationInfo("vortex_client_ui", title, PackSource.BUILT_IN, Optional.empty());
        // This client-only pack contains namespaced fonts, no vanilla replacements or data.
        var metadata = new Pack.Metadata(title, PackCompatibility.COMPATIBLE, FeatureFlagSet.of(), List.of());
        return new Pack(location, new PathPackResources.PathResourcesSupplier(root), metadata,
            new PackSelectionConfig(true, Pack.Position.TOP, true));
    }
}
