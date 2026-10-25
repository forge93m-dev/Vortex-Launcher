package dev.vortex.client.mixin;

import dev.vortex.client.PanoramaLibrary;
import net.minecraft.client.renderer.texture.CubeMapTexture;
import net.minecraft.client.renderer.texture.TextureContents;
import net.minecraft.resources.Identifier;
import net.minecraft.server.packs.resources.ResourceManager;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

@Mixin(CubeMapTexture.class)
public abstract class CubeMapTextureMixin {
    /** With an old panorama chosen, its six saved images replace the title panorama faces; everything else is vanilla. */
    @Redirect(method = "loadContents", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/renderer/texture/TextureContents;load(Lnet/minecraft/server/packs/resources/ResourceManager;Lnet/minecraft/resources/Identifier;)Lnet/minecraft/client/renderer/texture/TextureContents;"))
    private TextureContents vortex$panorama(ResourceManager resources, Identifier id) throws java.io.IOException {
        var file = "minecraft".equals(id.getNamespace()) ? PanoramaLibrary.override(id.getPath()) : null;
        if (file == null) return TextureContents.load(resources, id);
        try (var input = java.nio.file.Files.newInputStream(file)) {
            // Smooth (linear) sampling and clamped edges: a turning panorama would otherwise shimmer in steps.
            return new TextureContents(com.mojang.blaze3d.platform.NativeImage.read(input),
                new net.minecraft.client.resources.metadata.texture.TextureMetadataSection(true, true, net.minecraft.client.renderer.texture.MipmapStrategy.AUTO, 0f));
        }
    }
}
