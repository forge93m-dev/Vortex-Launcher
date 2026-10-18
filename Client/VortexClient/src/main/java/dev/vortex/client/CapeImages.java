package dev.vortex.client;

import com.mojang.blaze3d.platform.NativeImage;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.core.ClientAsset;
import net.minecraft.resources.Identifier;

/** GPU changes only on the Minecraft thread, bounded to the nearby player set. */
public final class CapeImages {
    private static final Map<String,ClientAsset.Texture> textures=new ConcurrentHashMap<>();
    private static final Map<String,Integer> widths=new ConcurrentHashMap<>();
    public static int width(String id) {return widths.getOrDefault(id,64);}
    private CapeImages() { }
    public static ClientAsset.Texture asset(String id) {return textures.get(id == null ? "" : id);}
    public static void install(String id, byte[] pixels) throws Exception {
        if(!id.equals(CapePixels.id(pixels))) throw new IllegalArgumentException("Cape checksum mismatch.");
        if(textures.containsKey(id)) return;
        if(textures.size()>=34) throw new IllegalStateException("Cape texture limit reached.");
        Identifier location=Identifier.fromNamespaceAndPath("vortex_client","uploaded_capes/"+id.substring(id.indexOf(':')+1));
        var decoded=CapePixels.unpack(pixels); byte[] rgba=decoded.rgba();
        var image=new NativeImage(decoded.width(),decoded.height(),false);
        DynamicTexture texture=null;
        try {
            for(int y=0;y<decoded.height();y++) for(int x=0;x<decoded.width();x++) {
                int p=(y*decoded.width()+x)*4;
                image.setPixel(x,y,((rgba[p+3]&255)<<24)|((rgba[p]&255)<<16)|((rgba[p+1]&255)<<8)|(rgba[p+2]&255));
            }
            texture=new DynamicTexture(()->"Vortex custom cape",image);
            Minecraft.getInstance().getTextureManager().register(location,texture);
            textures.put(id,new ClientAsset.ResourceTexture(location,location));
            widths.put(id,decoded.width());
        } catch(Exception error) {if(texture!=null) texture.close(); else image.close(); throw error;}
    }
    public static void retain(Set<String> keep) {
        for(String id:Set.copyOf(textures.keySet())) if(!keep.contains(id)) {
            if(Minecraft.getInstance().gui.screen() instanceof WardrobeScreen wardrobe && wardrobe.retainsCape(textures.get(id)))continue;
            var removed=textures.remove(id);
            widths.remove(id);
            if(removed!=null) Minecraft.getInstance().getTextureManager().release(removed.texturePath());
        }
    }
}
