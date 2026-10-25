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
    private record Anim(ClientAsset.Texture[] frames,int frameMs,long pixels) { }
    /** All animated capes on screen together may hold this many pixels of frames (about 96 MB), whatever players wear. */
    private static final long ANIMATED_BUDGET=24_000_000L;
    private static long animatedPixels;
    private static final Map<String,Anim> anims=new ConcurrentHashMap<>();
    public static boolean animated(String id) {return id!=null && anims.containsKey(id);}
    /** The frame to show now; every player wearing the same cape animates in step. */
    public static ClientAsset.Texture current(String id) {
        var anim=anims.get(id==null?"":id); if(anim==null) return asset(id);
        return anim.frames()[(int)Math.floorMod(System.currentTimeMillis()/anim.frameMs(),(long)anim.frames().length)];
    }
    /** Any frame of an uploaded animated cape as its first frame (so a fade sees one cape, not a new one per frame). */
    public static ClientAsset.Texture canonical(ClientAsset.Texture texture) {
        if(texture==null) return null;
        for(var anim:anims.values()) for(var frame:anim.frames()) if(frame.equals(texture)) return anim.frames()[0];
        return texture;
    }
    public static ClientAsset.Texture currentFor(ClientAsset.Texture first) {
        if(first==null) return null;
        for(var entry:anims.entrySet()) if(entry.getValue().frames()[0].equals(first)) return current(entry.getKey());
        return first;
    }
    private static void installAnimation(String id,byte[] pixels) throws Exception {
        var animation=CapePixels.unpackAnimation(pixels);
        long cost=(long)animation.width()*animation.height()*animation.frames().length;
        if(animatedPixels+cost>ANIMATED_BUDGET) throw new IllegalStateException("Too many animated capes are loaded at once.");
        var frames=new ClientAsset.Texture[animation.frames().length];
        var registered=new java.util.ArrayList<Identifier>();
        try {
            for(int i=0;i<frames.length;i++) {
                Identifier location=Identifier.fromNamespaceAndPath("vortex_client","uploaded_capes/"+id.substring(id.indexOf(':')+1)+"_"+i);
                byte[] rgba=animation.frames()[i];
                var image=new NativeImage(animation.width(),animation.height(),false);
                DynamicTexture texture=null;
                try {
                    for(int y=0;y<animation.height();y++) for(int x=0;x<animation.width();x++) {
                        int p=(y*animation.width()+x)*4;
                        image.setPixel(x,y,((rgba[p+3]&255)<<24)|((rgba[p]&255)<<16)|((rgba[p+1]&255)<<8)|(rgba[p+2]&255));
                    }
                    texture=new DynamicTexture(()->"Vortex animated cape",image);
                    Minecraft.getInstance().getTextureManager().register(location,texture);
                } catch(Exception error) {if(texture!=null) texture.close(); else image.close(); throw error;}
                registered.add(location);
                frames[i]=new ClientAsset.ResourceTexture(location,location);
            }
        } catch(Exception error) {
            for(var location:registered) Minecraft.getInstance().getTextureManager().release(location);
            throw error;
        }
        anims.put(id,new Anim(frames,animation.frameMs(),cost));
        animatedPixels+=cost;
        textures.put(id,frames[0]);
        widths.put(id,animation.width());
    }
    public static int width(String id) {return widths.getOrDefault(id,64);}
    private CapeImages() { }
    public static ClientAsset.Texture asset(String id) {return textures.get(id == null ? "" : id);}
    public static void install(String id, byte[] pixels) throws Exception {
        if(!id.equals(CapePixels.id(pixels))) throw new IllegalArgumentException("Cape checksum mismatch.");
        if(textures.containsKey(id)) return;
        if(textures.size()>=34) throw new IllegalStateException("Cape texture limit reached.");
        if(CapePixels.animated(pixels)) {installAnimation(id,pixels); return;}
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
            var animation=anims.remove(id);
            if(animation!=null) {animatedPixels-=animation.pixels(); for(var frame:animation.frames()) Minecraft.getInstance().getTextureManager().release(frame.texturePath()); continue;}
            if(removed!=null) Minecraft.getInstance().getTextureManager().release(removed.texturePath());
        }
    }
}
