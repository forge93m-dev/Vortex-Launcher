package dev.vortex.client;

import java.nio.ByteBuffer;
import java.nio.file.*;

/** Strict 64x64 PNG import and per-profile local storage; never modifies the source image. */
public final class SkinPixels {
    public record Selection(boolean enabled, boolean slim, byte[] rgba) { }
    public static final int SIZE=64*64*4;
    private SkinPixels() { }
    public static byte[] decode(byte[] png) throws Exception {
        if(png.length<33 || png.length>1024*1024 || ByteBuffer.wrap(png).getLong()!=0x89504e470d0a1a0aL ||
            ByteBuffer.wrap(png,16,4).getInt()!=64 || ByteBuffer.wrap(png,20,4).getInt()!=64)
            throw new IllegalArgumentException("Choose a Minecraft skin PNG, 64 x 64, up to 1 MB. Photos and cape textures are not skins.");
        var source=CapeArtwork.decode(png);
        byte[] pixels=source.rgba();
        // The base body must remain opaque; only the outer clothing layer may be transparent.
        for(int[] box:new int[][]{{0,0,32,16},{0,16,64,32},{16,48,48,64}})
            for(int y=box[1];y<box[3];y++)for(int x=box[0];x<box[2];x++)pixels[(y*64+x)*4+3]=(byte)255;
        return pixels;
    }
    public static void save(Path path,Selection selection) throws Exception {
        if(selection.rgba().length!=SIZE)throw new IllegalArgumentException("Invalid skin pixels.");
        Files.createDirectories(path.getParent()); checkPath(path);
        var data=ByteBuffer.allocate(SIZE+5).putInt(0x56534B31).put((byte)((selection.enabled()?2:0)|(selection.slim()?1:0))).put(selection.rgba()).array();
        Path temporary=Files.createTempFile(path.getParent(),"skin-",".tmp");
        try {Files.write(temporary,data);try {Files.move(temporary,path,StandardCopyOption.ATOMIC_MOVE,StandardCopyOption.REPLACE_EXISTING);}
            catch(AtomicMoveNotSupportedException error){Files.move(temporary,path,StandardCopyOption.REPLACE_EXISTING);}}
        finally {Files.deleteIfExists(temporary);}
    }
    public static Selection load(Path path) throws Exception {
        checkPath(path);
        if(!Files.exists(path))return null;
        if(Files.size(path)!=SIZE+5)throw new IllegalArgumentException("Saved skin is unreadable; original file was preserved.");
        var input=ByteBuffer.wrap(Files.readAllBytes(path));
        if(input.getInt()!=0x56534B31)throw new IllegalArgumentException("Invalid saved skin header.");
        int flags=input.get()&255;if(flags>3)throw new IllegalArgumentException("Invalid saved skin flags.");
        byte[] pixels=new byte[SIZE];input.get(pixels);return new Selection((flags&2)!=0,(flags&1)!=0,pixels);
    }
    private static void checkPath(Path path) throws Exception {
        for(Path p=path;p!=null;p=p.getParent())if(Files.isSymbolicLink(p))throw new IllegalArgumentException("Linked skin storage is not supported.");
    }
}
